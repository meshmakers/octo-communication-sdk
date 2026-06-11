namespace Meshmakers.Octo.Sdk.Common.EtlDataPipeline.Debugger;

/// <summary>
/// Represents the debug event arguments
/// </summary>
public class DebugEventArgs : EventArgs
{
    /// <summary>
    /// Represents the debug points
    /// </summary>
    public DebugInformationRoot DebugInformationRoot { get; }

    /// <summary>
    /// The debug event arguments
    /// </summary>
    /// <param name="debugInformationRoot"></param>
    public DebugEventArgs(DebugInformationRoot debugInformationRoot)
    {
        DebugInformationRoot = debugInformationRoot;
    }
}