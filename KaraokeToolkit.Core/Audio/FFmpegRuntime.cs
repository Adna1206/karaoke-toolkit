using FFMpegCore;
namespace KaraokeToolkit.Core.Audio;

/// <summary>Per-call FFmpeg configuration; no global state and no network downloads at application startup.</summary>
internal static class FFmpegRuntime
{
    internal static FFOptions Options(string workingDirectory)
    {
        string binaries = Path.Combine(AppContext.BaseDirectory, "ffmpeg", "bin");
        string suffix = OperatingSystem.IsWindows() ? ".exe" : "";
        if (!File.Exists(Path.Combine(binaries, "ffmpeg" + suffix)) || !File.Exists(Path.Combine(binaries, "ffprobe" + suffix)))
            throw new FileNotFoundException("Bundled FFmpeg is missing. Rebuild or reinstall " + ApplicationInfo.Name + " for this platform.");
        return new FFOptions { BinaryFolder = binaries, WorkingDirectory = workingDirectory, TemporaryFilesFolder = workingDirectory };
    }
    internal static string LocalPath(string path)
    {
        string full = Path.GetFullPath(path);
        // FFMpegCore builds a quoted command line. Reject literal quotes/newlines,
        // valid on Unix, rather than allowing them to alter process arguments.
        if (full.IndexOfAny(['"', '\r', '\n']) >= 0) throw new ArgumentException("Audio paths cannot contain quotes or line breaks.");
        return full;
    }
}