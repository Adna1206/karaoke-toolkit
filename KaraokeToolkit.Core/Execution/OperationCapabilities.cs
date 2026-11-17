namespace KaraokeToolkit.Core.Execution;

public enum OperationSupport
{
    Supported, Deferred, HardwareUnverified
}
/// <summary>Availability and execution capabilities for every built-in operation.</summary>
public static class OperationCapabilities
{
    public static OperationSupport Describe(OperationId operation) => OperationCatalog.Get(operation).Support;
    public static bool RenamesFilenames(OperationId operation) => operation is
        OperationId.CleanUpFilenames or OperationId.AddTrackIdSeparator or OperationId.RemoveTrackIdSeparator;
    public static bool IsConversion(OperationId operation) => OperationCatalog.Get(operation).IsConversion;
    public static bool CanRunUnattended(OperationId operation) => OperationCatalog.Get(operation).CanRunUnattended;
}
