using System.Reflection;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Configuration;
using Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Nodes.Triggers;
using Xunit;

namespace Sdk.Common.Tests.EtlDataPipeline.Configuration;

/// <summary>
///     AB#5228 — the guard that stops the next <c>LoxonePollTrigger@1</c>.
/// </summary>
/// <remarks>
///     <para>
///         A trigger whose firing depends on this adapter process staying alive (an in-process
///         polling loop, an in-memory event subscription, a socket the process itself listens on)
///         MUST carry <c>[NodeRequiresRunningProcess]</c>. Without it the communication controller
///         classifies the workload on-demand capable, hibernation scales it to zero, and the trigger
///         stops firing with no error and no alarm — data simply stops arriving (Epic AB#4914).
///     </para>
///     <para>
///         🔴 The allow-list below is the ONLY escape. Adding a trigger to it is a reviewed claim
///         that something OUTSIDE this process can wake a hibernated workload, with the wake path
///         named. It is not a place to park a trigger you have not thought about — the whole point
///         of this test is that an unreviewed new trigger fails the build instead of shipping a
///         silent hibernation bug.
///     </para>
/// </remarks>
public class TriggerProcessBoundGuardTests
{
    /// <summary>
    ///     Triggers reviewed and found NOT process-bound, each with the wake path that makes it so.
    ///     Keyed by qualified node name ("Name@Version").
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> ReviewedWakeCapableTriggers =
        new Dictionary<string, string>
        {
            // The controller wake-gates the send: TriggerManagementService.StartExecutePipelineAsync
            // calls EnsureWorkloadRunningForPipelineAsync and only then publishes onto the
            // (deliberately non-durable, auto-delete) execute-pipeline queue.
            ["FromExecutePipelineCommand@1"] =
                "Controller wakes the workload before the command send (AB#4918 wake gate).",

            // Pipeline chaining inside one DataFlow. Classified wake-capable by the on-demand design
            // (docs/concepts/on-demand-adapter-lifecycle.md §5) and pinned as such by the controller
            // test suite. ⚠️ AB#5228 note: the consumer queue is actually registered
            // Durable=false/AutoDelete=true (EventHubControl.RegisterRoutedEventConsumer with an
            // exchange), so a data event sent from a DIFFERENT workload to a hibernated one is
            // dropped. Reported for a follow-up decision — the fix belongs on the queue, not here.
            ["FromPipelineDataEvent@1"] =
                "Chaining within a DataFlow; classified wake-capable by the on-demand design."
        };

    public static TheoryData<string> TriggerConfigurationNames()
    {
        var data = new TheoryData<string>();
        foreach (var name in AllTriggerConfigurations().Select(QualifiedName).Order())
        {
            data.Add(name);
        }

        return data;
    }

    private static IEnumerable<Type> AllTriggerConfigurations()
    {
        // The SDK's own pipeline assembly, never the test assembly — test doubles carry node names
        // too and must not be able to satisfy (or break) this guard.
        return typeof(FromPollingNodeConfiguration).Assembly
            .GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false }
                        && typeof(ITriggerNodeConfiguration).IsAssignableFrom(t)
                        && t.GetCustomAttribute<NodeNameAttribute>() != null);
    }

    private static string QualifiedName(Type configurationType)
    {
        var nodeName = configurationType.GetCustomAttribute<NodeNameAttribute>()!;
        return $"{nodeName.Name}@{nodeName.Version}";
    }

    [Theory]
    [MemberData(nameof(TriggerConfigurationNames))]
    public void EveryTriggerConfiguration_IsEitherProcessBoundOrOnTheReviewedAllowList(string qualifiedName)
    {
        var configurationType = AllTriggerConfigurations().Single(t => QualifiedName(t) == qualifiedName);
        var isProcessBound =
            configurationType.GetCustomAttribute<NodeRequiresRunningProcessAttribute>() != null;
        var isReviewedWakeCapable = ReviewedWakeCapableTriggers.ContainsKey(qualifiedName);

        Assert.True(isProcessBound || isReviewedWakeCapable,
            $"Trigger '{qualifiedName}' ({configurationType.FullName}) carries neither " +
            $"[{nameof(NodeRequiresRunningProcessAttribute)}] nor an entry on the reviewed " +
            "wake-capable allow-list in this test. Read its StartAsync/StopAsync: if it only fires " +
            "while the adapter process is alive, add the attribute; if something external can wake a " +
            "hibernated workload, add it to ReviewedWakeCapableTriggers WITH the wake path named. " +
            "Getting this wrong silently stops the trigger under scale-to-zero (AB#4914 / AB#5228).");

        Assert.False(isProcessBound && isReviewedWakeCapable,
            $"Trigger '{qualifiedName}' is marked [{nameof(NodeRequiresRunningProcessAttribute)}] " +
            "and is also on the reviewed wake-capable allow-list. Exactly one of the two is true.");
    }

    [Fact]
    public void AllowList_HasNoStaleEntries()
    {
        var known = AllTriggerConfigurations().Select(QualifiedName).ToHashSet();
        var stale = ReviewedWakeCapableTriggers.Keys.Where(k => !known.Contains(k)).ToList();

        Assert.True(stale.Count == 0,
            $"Allow-list entries with no matching trigger configuration: {string.Join(", ", stale)}. " +
            "A renamed or deleted trigger must not leave a blanket exemption behind.");
    }

    [Fact]
    public void FromPolling_IsProcessBound()
    {
        // The canonical in-process polling trigger — the one the guard exists to protect.
        Assert.NotNull(typeof(FromPollingNodeConfiguration)
            .GetCustomAttribute<NodeRequiresRunningProcessAttribute>());
    }
}
