using KaraokeToolkit.Core.Conversion;
namespace KaraokeToolkit.Core.Video;

public enum VideoContainer { MP4, MKV, AVI }
public enum VideoResolution { SD, HD, FullHD, QHD, UHD }
public enum VideoGraphicsSmoothing { None, Bilinear, Bicubic, Lanczos }

internal static class VideoOptions
{
    internal static string Extension(VideoContainer container) => container switch
    {
        VideoContainer.MP4 => ".mp4",
        VideoContainer.MKV => ".mkv",
        VideoContainer.AVI => ".avi",
        _ => throw new ArgumentOutOfRangeException(nameof(container))
    };
    internal static (int Width, int Height) Size(VideoResolution resolution) => resolution switch
    {
        VideoResolution.SD => (640, 480),
        VideoResolution.HD => (1280, 720),
        VideoResolution.FullHD => (1920, 1080),
        VideoResolution.QHD => (2560, 1440),
        VideoResolution.UHD => (3840, 2160),
        _ => throw new ArgumentOutOfRangeException(nameof(resolution))
    };
    /// <summary>None preserves the original pixel edges; the other algorithms interpolate them.</summary>
    internal static string ScalingAlgorithm(VideoGraphicsSmoothing smoothing) => smoothing switch
    {
        VideoGraphicsSmoothing.None => "neighbor",
        VideoGraphicsSmoothing.Bilinear => "bilinear",
        VideoGraphicsSmoothing.Bicubic => "bicubic",
        VideoGraphicsSmoothing.Lanczos => "lanczos",
        _ => throw new ArgumentOutOfRangeException(nameof(smoothing))
    };
    internal static bool SupportsFrameRate(int value) => value is 25 or 30 or 60;
    internal static bool SupportsVideoBitrate(int value) => value is 1000 or 2000 or 4000 or 6000;
    internal static bool SupportsAudioBitrate(int value) => value is 128 or 192 or 256 or 320;

    internal static void Validate(ConversionOptions options)
    {
        _ = Extension(options.VideoContainer);
        _ = Size(options.VideoResolution);
        _ = ScalingAlgorithm(options.VideoGraphicsSmoothing);
        if (!SupportsFrameRate(options.VideoFrameRate)) throw new ArgumentOutOfRangeException(nameof(options.VideoFrameRate));
        if (!SupportsVideoBitrate(options.VideoBitrateKbps)) throw new ArgumentOutOfRangeException(nameof(options.VideoBitrateKbps));
        if (!SupportsAudioBitrate(options.VideoAudioBitrateKbps)) throw new ArgumentOutOfRangeException(nameof(options.VideoAudioBitrateKbps));
    }
}
