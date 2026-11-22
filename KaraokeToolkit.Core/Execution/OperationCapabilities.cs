using KaraokeToolkit.Core.Recovery;

namespace KaraokeToolkit.Core.Execution;

public enum OperationSupport
{
    Supported, Deferred, HardwareUnverified
}
/// <summary>Availability and execution capabilities for every built-in operation.</summary>
public static class OperationCapabilities
{
    public const RecoveryVerificationMode DefaultFileMoveRecoveryVerification = RecoveryVerificationMode.XxHash128;

    public static OperationSupport Describe(OperationId operation) => OperationCatalog.Get(operation).Support;
    public static bool RenamesFilenames(OperationId operation) => operation is
        OperationId.CleanUpFilenames or OperationId.AddTrackIdSeparator or OperationId.RemoveTrackIdSeparator;
    public static bool IsConversion(OperationId operation) => OperationCatalog.Get(operation).IsConversion;
    public static bool CanRunUnattended(OperationId operation) => OperationCatalog.Get(operation).CanRunUnattended;
    public static bool SupportsFileMoveRecoveryOptions(OperationId operation) => operation is
        OperationId.GroupByTrackId or OperationId.OrganizeFiles or OperationId.OrganizeFolders
        or OperationId.FlattenOneLayer or OperationId.FlattenAllLayers;
    public static RecoveryVerificationMode DefaultRecoveryVerification(OperationId operation) =>
        SupportsFileMoveRecoveryOptions(operation) ? DefaultFileMoveRecoveryVerification : RecoveryVerificationMode.Sha256;
}
