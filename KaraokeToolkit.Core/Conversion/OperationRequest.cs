using KaraokeToolkit.Core.Video;
using KaraokeToolkit.Core.Audio;
using KaraokeToolkit.Core.Archives;
namespace KaraokeToolkit.Core.Conversion;

/// <summary>Compatibility request retaining original wire identifiers and settings names.</summary>
public sealed record OperationRequest(
    OperationCatalog.defAction Action, string Folder, ConversionOptions Options)
{
    // Preserved controller fields; passing them is not evidence of working optical track selection.
    public string DiscNumber { get; init; } = "";
    public string OutputFolder { get; init; } = "";
    public string TracksToInclude { get; init; } = "All";

    /// <summary>Checks request shape; filesystem and runtime checks happen in the service.</summary>
    /// <remarks>This does not certify action support. Consult capability metadata and known engine limitations.</remarks>
    public void Validate()
    {
        if (!Enum.IsDefined(Action))
            throw new ArgumentOutOfRangeException(nameof(Action));
        ArgumentNullException.ThrowIfNull(Options);
        if (Action is OperationCatalog.defAction.Play_AudioG or OperationCatalog.defAction.Check_For_Internet_Update)
            throw new NotSupportedException("Playback and update launching require application-host integration, not the batch controller.");
        if (string.IsNullOrWhiteSpace(Folder))
            throw new ArgumentException("A source folder is required.", nameof(Folder));
        if (!Enum.IsDefined(Options.AudioEncoder))
            throw new ArgumentOutOfRangeException(nameof(Options.AudioEncoder));
        if (!Enum.IsDefined(Options.ZipAlgorithm))
            throw new ArgumentOutOfRangeException(nameof(Options.ZipAlgorithm));
        if (!Enum.IsDefined(Options.ZipCompression))
            throw new ArgumentOutOfRangeException(nameof(Options.ZipCompression));
        if (Action == OperationCatalog.defAction.Karaoke_To_Video)
            VideoOptions.Validate(Options);
        if ((Action == OperationCatalog.defAction.LosslessG_To_Mp3G || AudioEncoderService.UsesBitrate(Options.AudioEncoder)) &&
            (Options.AudioBitrate is <= 0 or > short.MaxValue))
            throw new ArgumentOutOfRangeException(nameof(Options.AudioBitrate));
    }
}

