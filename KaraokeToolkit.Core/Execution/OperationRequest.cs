using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Tagging;
using KaraokeToolkit.Core.FileOperations;
using KaraokeToolkit.Core.Recovery;

namespace KaraokeToolkit.Core.Execution;

/// <summary>General per-invocation contract. Interface preferences and command names
/// are not part of the execution request. No desktop settings are read or modified.</summary>
public sealed record OperationRequest(OperationId OperationId, IReadOnlyList<string> Inputs)
{
    private RecoveryVerificationMode? recoveryVerification;
    private FileCheckTiming? checkTiming;

    public ConversionOptions Options { get; init; } = new() { DeleteOldFiles = false };
    public OverwritePolicy OverwritePolicy { get; init; } = OverwritePolicy.Fail;
    public bool ValidateBeforeConversion { get; init; }
    public bool IgnoreLeadingThe { get; init; }
    public string TaggingPattern { get; init; } = FilenameTagPattern.Default;
    public TaggingOptions Tagging { get; init; } = new();
    public bool SaveResumableRun { get; init; } = true;
    public FileCheckTiming CheckTiming
    {
        get => checkTiming ?? (OperationCapabilities.SupportsFileCheckTiming(OperationId)
            ? OperationCapabilities.DefaultFileMoveCheckTiming : FileCheckTiming.BeforeBatch);
        init => checkTiming = value;
    }
    public RecoveryVerificationMode RecoveryVerification
    {
        get => recoveryVerification ?? OperationCapabilities.DefaultRecoveryVerification(OperationId);
        init => recoveryVerification = value;
    }
    public TrackGroupingOptions Grouping { get; init; } = new();
    public string OutputFolder { get; init; } = "";
}
