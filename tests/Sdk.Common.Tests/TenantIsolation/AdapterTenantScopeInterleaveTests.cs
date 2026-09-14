using System.Collections.Concurrent;
using Meshmakers.Octo.Sdk.Common.Services;

namespace Sdk.Common.Tests.TenantIsolation;

/// <summary>
///     AB#4924 increment 3 — interleaving and the poison canary, at the level the refactor actually
///     introduces (verification mechanisms 3, 4 and 5, in their scope-level form).
/// </summary>
/// <remarks>
///     <para>
///         🔴 <b>Read this before trusting these tests.</b> The plan's mechanisms 3–5 call for a
///         two-tenant interleave over real MongoDB asserting on <b>pipeline output</b>. That test
///         cannot be written in increment 3 and these are not it — see
///         <c>docs/…/shared-adapter-leasing-implementation.md</c> §5.5. What these DO cover is the
///         mechanism the refactor introduces and the one place it can go wrong on its own: whether
///         the per-execution tenant follows an async call chain correctly under concurrency, and
///         whether it leaks between chains.
///     </para>
///     <para>
///         That is worth testing on its own terms. <see cref="AsyncLocal{T}" /> flows into
///         continuations but is copy-on-write across them, and getting that wrong produces exactly
///         the failure this whole increment exists to prevent — an execution observing another
///         tenant's value — while a single-tenant test suite stays green.
///     </para>
/// </remarks>
public class AdapterTenantScopeInterleaveTests
{
    private static IAdapterTenantScope CreateScope() => new AdapterTenantScope();

    [Fact]
    public void NoTenantIsInScopeOutsideAnExecution()
    {
        var scope = CreateScope();

        Assert.False(scope.HasTenant);

        // Deliberately an exception, not null: a caller reaching for the tenant outside an
        // execution has a bug, and the one outcome that must never happen is a plausible-looking
        // wrong answer.
        Assert.Throws<InvalidOperationException>(() => scope.TenantId);
    }

    [Fact]
    public void TheTenantIsClearedWhenTheExecutionEnds()
    {
        var scope = CreateScope();

        using (scope.BeginExecution("tenantA"))
        {
            Assert.Equal("tenantA", scope.TenantId);
        }

        Assert.False(scope.HasTenant);
    }

    [Fact]
    public void ASubPipelineRestoresTheOuterTenantRatherThanClearingIt()
    {
        // A node that starts a sub-pipeline runs inside the outer execution. If the inner scope
        // cleared instead of restoring, the remainder of the outer pipeline would run with no
        // tenant and fail in a way that points nowhere near the nesting.
        var scope = CreateScope();

        using (scope.BeginExecution("outer"))
        {
            using (scope.BeginExecution("inner"))
            {
                Assert.Equal("inner", scope.TenantId);
            }

            Assert.Equal("outer", scope.TenantId);
        }

        Assert.False(scope.HasTenant);
    }

    [Fact]
    public async Task ConcurrentExecutionsEachSeeOnlyTheirOwnTenant()
    {
        // The core of it. Twelve tenants, many interleaved awaits each, all on one process — the
        // shape of a pool member serving a rotation. Every observation is recorded and every
        // observation must match the tenant that execution entered.
        var scope = CreateScope();
        var violations = new ConcurrentBag<string>();

        var executions = Enumerable.Range(0, 12).Select(async i =>
        {
            var expected = $"tenant{i:00}";
            using (scope.BeginExecution(expected))
            {
                for (var step = 0; step < 25; step++)
                {
                    // Yield hands the continuation to another thread pool thread, which is exactly
                    // where an AsyncLocal that is captured rather than flowed would go wrong.
                    await Task.Yield();
                    await Task.Delay(Random.Shared.Next(0, 3), TestContext.Current.CancellationToken);

                    var observed = scope.TenantId;
                    if (!string.Equals(observed, expected, StringComparison.Ordinal))
                    {
                        violations.Add($"execution for {expected} observed {observed} at step {step}");
                    }
                }
            }
        }).ToArray();

        await Task.WhenAll(executions);

        Assert.True(violations.IsEmpty,
            "A pipeline execution observed another tenant's id. This is the cross-tenant failure "
            + "mode increment 3 exists to prevent. Violations: " + string.Join("; ", violations));
        Assert.False(scope.HasTenant);
    }

    [Fact]
    public async Task ThePoisonCanaryNeverCrosses()
    {
        // Verification mechanism 4. One tenant carries a value that must never be observed by any
        // other execution. Cheap, and unlike the assertion above it fails loudly on a leak nobody
        // predicted — it does not require the test author to have guessed which tenant would leak.
        const string canaryTenant = "tenant-with-canary-4f3a9c";
        var scope = CreateScope();
        var sightings = new ConcurrentBag<string>();

        var canary = Task.Run(async () =>
        {
            using (scope.BeginExecution(canaryTenant))
            {
                for (var i = 0; i < 50; i++)
                {
                    await Task.Yield();
                    _ = scope.TenantId;
                }
            }
        }, TestContext.Current.CancellationToken);

        var others = Enumerable.Range(0, 8).Select(i => Task.Run(async () =>
        {
            using (scope.BeginExecution($"ordinary{i:00}"))
            {
                for (var step = 0; step < 50; step++)
                {
                    await Task.Yield();
                    if (scope.TenantId.Contains("canary", StringComparison.Ordinal))
                    {
                        sightings.Add($"ordinary{i:00} saw the canary at step {step}");
                    }
                }
            }
        }, TestContext.Current.CancellationToken)).ToArray();

        await Task.WhenAll(others.Append(canary));

        Assert.True(sightings.IsEmpty,
            "The poison canary crossed into another execution: " + string.Join("; ", sightings));
    }

    [Fact]
    public async Task RandomisedInterleavingsHoldOverManyRuns()
    {
        // Verification mechanism 5. A race that shows up once in fifty executions is precisely the
        // failure mode of a shared process, so the assertion is repeated rather than run once.
        // 200 executions over 4 tenants in randomised order, each asserting it saw only its own.
        var scope = CreateScope();
        var tenants = new[] { "alpha", "bravo", "charlie", "delta" };
        var violations = new ConcurrentBag<string>();

        var executions = Enumerable.Range(0, 200).Select(i => Task.Run(async () =>
        {
            var expected = tenants[Random.Shared.Next(tenants.Length)];
            using (scope.BeginExecution(expected))
            {
                await Task.Yield();
                if (Random.Shared.Next(4) == 0)
                {
                    await Task.Delay(1, TestContext.Current.CancellationToken);
                }

                var observed = scope.TenantId;
                if (!string.Equals(observed, expected, StringComparison.Ordinal))
                {
                    violations.Add($"run {i}: expected {expected}, observed {observed}");
                }
            }
        }, TestContext.Current.CancellationToken)).ToArray();

        await Task.WhenAll(executions);

        Assert.True(violations.IsEmpty,
            "Randomised interleaving observed a foreign tenant: " + string.Join("; ", violations));
    }

    [Fact]
    public void APoolMemberCannotBeEnabledByConfiguration()
    {
        // Increment 3 ships as a behaviour-preserving refactor that the fleet bakes for one release
        // before any lease exists. A flag that could flip IsPoolMember from the environment would
        // defeat the point of baking it, so the property is hard-coded and this pins that.
        Assert.False(CreateScope().IsPoolMember);
    }

    [Fact]
    public void AnEmptyTenantIsRefusedRatherThanEntered()
    {
        var scope = CreateScope();

        Assert.Throws<ArgumentException>(() => scope.BeginExecution(""));
        Assert.Throws<ArgumentException>(() => scope.BeginExecution("   "));
        Assert.False(scope.HasTenant);
    }
}
