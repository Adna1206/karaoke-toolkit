using KaraokeToolkit.Core.Audio;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Execution;
using KaraokeToolkit.Core.Recovery;
using KaraokeToolkit.Core.Tagging;

namespace KaraokeToolkit.Core.Settings;

public sealed record SharedSettingsSnapshot(
    ConversionOptions Options,
    AudioBitratePreferences AudioBitrates,
    OverwritePolicy Overwrite,
    bool ValidateBeforeConversion,
    bool IgnoreLeadingThe,
    string TaggingPattern,
    RecoveryVerificationMode RecoveryVerification,
    bool SaveResumableRun)
{
    public OperationRequest ForUnattendedOperation(OperationId id, IReadOnlyList<string> inputs)
    {
        bool supportsRecoveryOptions = OperationCapabilities.SupportsFileMoveRecoveryOptions(id);
        return new(id, inputs)
        {
            Options = Options with { DeleteOldFiles = false },
            OverwritePolicy = id == OperationId.GroupByTrackId || Overwrite == OverwritePolicy.Ask ? OverwritePolicy.Fail : Overwrite,
            ValidateBeforeConversion = ValidateBeforeConversion,
            IgnoreLeadingThe = id == OperationId.OrganizeFiles && IgnoreLeadingThe,
            TaggingPattern = TaggingPattern,
            RecoveryVerification = supportsRecoveryOptions ? RecoveryVerification : RecoveryVerificationMode.Sha256,
            SaveResumableRun = !supportsRecoveryOptions || SaveResumableRun
        };
    }

    public int BitrateFor(AudioEncoder encoder, int fallback) =>
        AudioEncoderService.UsesBitrate(encoder) ? AudioBitrates.For(encoder) : fallback;
}
