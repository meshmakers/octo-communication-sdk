using Meshmakers.Octo.Communication.Contracts.DataTransferObjects;
using Meshmakers.Octo.Communication.Contracts.Hubs;
using Microsoft.Extensions.DependencyInjection;

namespace Meshmakers.Octo.Sdk.Common.Adapters;

/// <summary>
///     Hands the hub client a callbacks target it can be constructed with, and resolves the real one
///     — <see cref="AdapterPoolClient" /> — only when a callback actually arrives.
/// </summary>
/// <remarks>
///     <para>
///         🔴 <b>This exists to break a dependency cycle that does not announce itself.</b>
///         <c>AdapterPoolClient</c> needs <c>IAdapterPoolHubClient</c> to talk to the controller;
///         <c>AdapterPoolHubClient</c> needs an <see cref="IAdapterPoolHubCallbacks" /> at
///         construction; and that used to be registered as a factory resolving
///         <c>AdapterPoolClient</c>. A closed loop.
///     </para>
///     <para>
///         Microsoft DI detects cycles by walking constructor parameters, and it cannot see through
///         a factory lambda — so nothing threw. Worse, <c>StackGuard.RunOnEmptyStack</c> keeps moving
///         the recursion onto fresh stacks, so it never even overflowed: the member process printed
///         two startup lines and then sat silent for ever, burning 0.6 s of CPU in ten minutes. It
///         was diagnosed with <c>dotnet-dump</c> during the first end-to-end lease run, because there
///         was nothing in any log to diagnose.
///     </para>
///     <para>
///         The resolution is deliberately late rather than cached: a callback arrives long after the
///         graph is built, so there is no window in which the cycle can re-form, and holding the
///         provider rather than the client keeps this class free of the lifetime question.
///     </para>
/// </remarks>
internal sealed class DeferredAdapterPoolHubCallbacks : IAdapterPoolHubCallbacks
{
    private readonly IServiceProvider _serviceProvider;

    public DeferredAdapterPoolHubCallbacks(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    private AdapterPoolClient Target => _serviceProvider.GetRequiredService<AdapterPoolClient>();

    /// <inheritdoc />
    public Task LeaseAsync(LeaseDto lease) => Target.LeaseAsync(lease);

    /// <inheritdoc />
    public Task DrainAsync(string reason) => Target.DrainAsync(reason);
}
