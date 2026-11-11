using KaraokeToolkit.Core.Video;
using KaraokeToolkit.Core.Audio;
using KaraokeToolkit.Core.Archives;
using System.Text.Json.Serialization;
namespace KaraokeToolkit.Core.Conversion;

/// <summary>Conversion preferences persisted in settings and recovery journals.</summary>
[JsonConverter(typeof(ConversionOptionsJsonConverter))]
public sealed record ConversionOptions
{
    public const int DefaultAudioBitrate = 320;
    public bool ExperimentalDiscRipping { get; init; }
    public string CdrdaoExecutablePath { get; init; } = "";
    public int AudioBitrate { get; init; } = DefaultAudioBitrate;
    public AudioEncoder AudioEncoder { get; init; } = AudioEncoder.MP3;
    public bool AutoCreateCueText { get; init; } = true;
    public string TrackListingName { get; init; } = FileOperations.MetadataFileNames.DefaultTemplate;
    public string CueSheetName { get; init; } = FileOperations.MetadataFileNames.DefaultTemplate;
    public bool AutoRenameZip { get; init; } = true;
    public bool DeleteOldFiles { get; init; } = true;
    public string LastDiscNumber { get; init; } = "";
    public string LastOutputFolder { get; init; } = "";
    public string NamingFormat { get; init; } = "DISC-TRACK ARTIST - TITLE";
    public bool RecurseFolders { get; init; } = true;
    public bool RenameBins { get; init; } = true;
    public ZipAlgorithm ZipAlgorithm { get; init; } = ZipAlgorithm.Deflate;
    public ZipCompression ZipCompression { get; init; } = ZipCompression.Balanced;
    public VideoContainer VideoContainer { get; init; } = VideoContainer.MP4;
    public VideoResolution VideoResolution { get; init; } = VideoResolution.HD;
    public VideoGraphicsSmoothing VideoGraphicsSmoothing { get; init; } = VideoGraphicsSmoothing.None;
    public int VideoFrameRate { get; init; } = 30;
    public int VideoBitrateKbps { get; init; } = 2000;
    public bool VideoAudioMatchSourceBitrate { get; init; } = true;
    public int VideoAudioBitrateKbps { get; init; } = 192;
}


