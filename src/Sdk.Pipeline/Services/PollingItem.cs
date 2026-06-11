namespace Meshmakers.Octo.Sdk.Common.Services;

internal record PollingItem
{
    public required Func<Task> Action { get; init; }
    public required TimeSpan Interval { get; init; }
    public required DateTime LastExecutionTime { get; set; }
    
    public required Timer? Timer { get; init; }
}