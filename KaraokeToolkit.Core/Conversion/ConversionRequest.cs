using KaraokeToolkit.Core.Execution;
using KaraokeToolkit.Core.Video;
using KaraokeToolkit.Core.Audio;
using KaraokeToolkit.Core.Archives;
using KaraokeToolkit.Core.FileOperations;
namespace KaraokeToolkit.Core.Conversion;

/// <summary>A conversion engine request. Action is persisted by name, independently of enum ordering.</summary>
public sealed record ConversionRequest(
    OperationId Action, string Folder, ConversionOptions Options)
{
    // Preserved controller fields; passing them is not evidence of working optical track selection.
    public string DiscNumber { get; init; } = "";
    public string OutputFolder { get; init; } = "";
    public string TracksToInclude { get; init; } = "All";

    /// <summary>Checks request shape and conversion routing; filesystem and native
    /// runtime checks happen in the conversion service.</summary>
    public void Validate()
    {
        if (!Enum.IsDefined(Action))
            throw new ArgumentOutOfRangeException(nameof(Action));
        ArgumentNullException.ThrowIfNull(Options);
        if (!OperationCapabilities.IsConversion(Action))
            throw new NotSupportedException("This operation is not handled by the conversion engine.");
        if (string.IsNullOrWhiteSpace(Folder))
            throw new ArgumentException("A source folder is required.", nameof(Folder));
        MetadataFileNames.Resolve(Options.TrackListingName, "Example folder", ".txt");
        MetadataFileNames.Resolve(Options.CueSheetName, "Example folder", ".cue");
        if (!string.IsNullOrEmpty(OutputFolder))
        {
            if (Action is OperationId.CleanUpFilenames or OperationId.CreateCueAndTextFiles)
                throw new ArgumentException("Folder maintenance does not support a separate output folder.");
            if (Options.DeleteOldFiles)
                throw new ArgumentException("A separate output folder requires keeping originals.");
            if (!Path.IsPathFullyQualified(OutputFolder) || !Directory.Exists(OutputFolder))
                throw new ArgumentException("Choose an existing absolute output folder.");
            KaraokeToolkit.Core.Recovery.RunSession.CheckPlainPath(OutputFolder);
        }
        if (!Enum.IsDefined(Options.AudioEncoder))
            throw new ArgumentOutOfRangeException(nameof(Options.AudioEncoder));
        if (!Enum.IsDefined(Options.ZipAlgorithm))
            throw new ArgumentOutOfRangeException(nameof(Options.ZipAlgorithm));
        if (!Enum.IsDefined(Options.ZipCompression))
            throw new ArgumentOutOfRangeException(nameof(Options.ZipCompression));
        if (Action == OperationId.KaraokeToVideo)
            VideoOptions.Validate(Options);
        if ((Action == OperationId.LosslessGToMp3G || AudioEncoderService.UsesBitrate(Options.AudioEncoder)) &&
            (Options.AudioBitrate is <= 0 or > short.MaxValue))
            throw new ArgumentOutOfRangeException(nameof(Options.AudioBitrate));
    }
}

