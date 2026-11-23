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
    public const FileCheckTiming DefaultFileMoveCheckTiming = FileCheckTiming.BeforeItem;

    public static OperationSupport Describe(OperationId operation) => OperationCatalog.Get(operation).Support;
    public static bool RenamesFilenames(OperationId operation) => operation is
        OperationId.CleanUpFilenames or OperationId.AddTrackIdSeparator or OperationId.RemoveTrackIdSeparator or OperationId.RenameByPattern;
    public static bool IsConversion(OperationId operation) => OperationCatalog.Get(operation).IsConversion;
    public static bool CanRunUnattended(OperationId operation) => OperationCatalog.Get(operation).CanRunUnattended;
    public static bool SupportsFileMoveRecoveryOptions(OperationId operation) => operation is
        OperationId.GroupByTrackId or OperationId.OrganizeFiles or OperationId.OrganizeFolders
        or OperationId.FlattenOneLayer or OperationId.FlattenAllLayers or OperationId.RenameByPattern;
    public static bool SupportsConversionCheckTiming(OperationId operation) =>
        IsConversion(operation) && !OperationCatalog.Get(operation).RequiresFolders;
    public static bool SupportsFileCheckTiming(OperationId operation) =>
        SupportsFileMoveRecoveryOptions(operation) || SupportsConversionCheckTiming(operation);
    public static bool SupportsBatchContinuation(OperationId operation) =>
        SupportsFileMoveRecoveryOptions(operation) || operation == OperationId.TagKaraoke ||
        IsConversion(operation) && Describe(operation) == OperationSupport.Supported;
    public static RecoveryVerificationMode DefaultRecoveryVerification(OperationId operation) =>
        SupportsFileCheckTiming(operation) ? DefaultFileMoveRecoveryVerification : RecoveryVerificationMode.Sha256;
}
