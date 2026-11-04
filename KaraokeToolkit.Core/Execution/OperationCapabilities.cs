namespace KaraokeToolkit.Core.Execution;

public enum OperationSupport
{
    Supported, Deferred, HardwareUnverified
}
/// <summary>Availability and execution capabilities for every built-in operation.</summary>
public static class OperationCapabilities
{
    public static OperationSupport Describe(OperationId operation) => OperationCatalog.Get(operation).Support;
    public static bool IsConversion(OperationId operation) => OperationCatalog.Get(operation).IsConversion;
    public static bool CanRunUnattended(OperationId operation) => OperationCatalog.Get(operation).CanRunUnattended;
}
