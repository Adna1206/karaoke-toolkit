using System.ComponentModel;
using System.Text.Json;
using KaraokeToolkit.Core.Audio;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Execution;
using KaraokeToolkit.Core.FileOperations;
using KaraokeToolkit.Core.Recovery;
using KaraokeToolkit.Core.Settings;
using KaraokeToolkit.Core.Tagging;

namespace KaraokeToolkit.Mcp;

/// <summary>Keeps omitted request fields distinct from explicit values until the profile is resolved.</summary>
public sealed record McpOperationRequest
{
    [Description("Use the name returned by get_operations, such as group-by-alphabet-files.")]
    public required OperationId OperationId { get; init; }
    public required IReadOnlyList<string> Inputs { get; init; }
    public JsonElement? Options { get; init; }
    public OverwritePolicy? OverwritePolicy { get; init; }
    public bool? ValidateBeforeConversion { get; init; }
    public bool? IgnoreLeadingThe { get; init; }
    public string? TaggingPattern { get; init; }
    public TaggingOptions? Tagging { get; init; }
    public bool? SaveResumableRun { get; init; }
    public RecoveryVerificationMode? RecoveryVerification { get; init; }
    public TrackGroupingOptions? Grouping { get; init; }
    public string? OutputFolder { get; init; }

    internal OperationRequest Resolve(SharedSettingsSnapshot profile)
    {
        var request = profile.ForUnattendedOperation(OperationId, Inputs);
        var processing = Options is { } explicitOptions
            ? McpConversionOptionsConverter.ReadOptions(explicitOptions, request.Options, Program.JsonOptions)
            : request.Options;
        if (Options is not { } supplied || !supplied.EnumerateObject()
            .Any(property => property.Name.Equals(nameof(ConversionOptions.AudioBitrate), StringComparison.OrdinalIgnoreCase)))
        {
            var encoder = OperationId == OperationId.LosslessGToMp3G ? AudioEncoder.MP3 : processing.AudioEncoder;
            processing = processing with { AudioBitrate = profile.BitrateFor(encoder, processing.AudioBitrate) };
        }
        return request with
        {
            Options = processing,
            Grouping = Grouping ?? request.Grouping,
            OverwritePolicy = OverwritePolicy ?? request.OverwritePolicy,
            ValidateBeforeConversion = ValidateBeforeConversion ?? request.ValidateBeforeConversion,
            IgnoreLeadingThe = IgnoreLeadingThe ?? request.IgnoreLeadingThe,
            TaggingPattern = TaggingPattern ?? request.TaggingPattern,
            Tagging = Tagging ?? request.Tagging,
            RecoveryVerification = RecoveryVerification ?? request.RecoveryVerification,
            SaveResumableRun = SaveResumableRun ?? request.SaveResumableRun,
            OutputFolder = OutputFolder ?? request.OutputFolder
        };
    }
}
