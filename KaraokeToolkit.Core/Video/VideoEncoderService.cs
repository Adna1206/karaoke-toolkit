using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Audio;
using KaraokeToolkit.Core.Archives;
using System.Diagnostics;
using FFMpegCore;

namespace KaraokeToolkit.Core.Video;

/// <summary>Decodes CD+G packets with FFmpeg and muxes the rendered frames with their audio.</summary>
internal static class VideoEncoderService
{
    internal static async Task EncodeAsync(string graphics, string audio, string output, ConversionOptions settings, CancellationToken token)
    {
        VideoOptions.Validate(settings);
        KaraokeArchive.ValidateGraphics(graphics);
        if (File.Exists(output)) throw new IOException("Video output already exists.");
        var options = FFmpegRuntime.Options(Path.GetDirectoryName(output)!);
        string ffmpeg = Path.Combine(options.BinaryFolder, OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");
        string videoCodec = settings.VideoContainer == VideoContainer.AVI ? "mpeg4" : await ChooseH264Async(ffmpeg, token);
        string audioCodec = settings.VideoContainer == VideoContainer.AVI ? "libmp3lame" : "aac";
        int audioBitrate = await ResolveAudioBitrateKbpsAsync(audio, settings, options, token);
        var sourceInfo = await MediaProbe.AnalyseAsync(audio, options, token);
        double audioSeconds = sourceInfo.Duration.TotalSeconds;
        if (!double.IsFinite(audioSeconds) || audioSeconds <= 0)
            throw new InvalidDataException("Input audio has no usable duration.");
        (int width, int height) = VideoOptions.Size(settings.VideoResolution);
        string scaling = VideoOptions.ScalingAlgorithm(settings.VideoGraphicsSmoothing);
        string filter = $"fps={settings.VideoFrameRate},scale={width}:{height}:force_original_aspect_ratio=decrease:flags={scaling}," +
            $"pad={width}:{height}:(ow-iw)/2:(oh-ih)/2:black,setsar=1,format=yuv420p," +
            "tpad=stop_mode=clone:stop_duration=86400";
        var args = new[]
        {
            "-nostdin", "-hide_banner", "-loglevel", "error", "-xerror", "-n",
            "-f", "cdg", "-i", FFmpegRuntime.LocalPath(graphics), "-i", FFmpegRuntime.LocalPath(audio),
            "-map", "0:v:0", "-map", "1:a:0", "-vf", filter,
            "-c:v", videoCodec, "-b:v", settings.VideoBitrateKbps + "k", "-pix_fmt", "yuv420p",
            "-fps_mode:v", "cfr", "-g:v", settings.VideoFrameRate.ToString(),
            "-keyint_min:v", settings.VideoFrameRate.ToString(), "-sc_threshold:v", "0", "-bf:v", "0",
            // -shortest alone can overshoot when the cloned video stream queues
            // frames ahead of audio. Keep the explicit limit independent of scheduling.
            "-c:a", audioCodec, "-b:a", audioBitrate + "k", "-shortest",
            "-t", audioSeconds.ToString("0.########", System.Globalization.CultureInfo.InvariantCulture)
        }.ToList();
        if (settings.VideoContainer == VideoContainer.MP4) { args.Add("-movflags"); args.Add("+faststart"); }
        args.Add(FFmpegRuntime.LocalPath(output));
        await RunAsync(ffmpeg, args, Path.GetDirectoryName(output)!, token);
        token.ThrowIfCancellationRequested();
        if (!File.Exists(output) || new FileInfo(output).Length == 0)
            throw new InvalidDataException("FFmpeg produced no video. Check that the CDG contains renderable graphics packets.");
        var info = await MediaProbe.AnalyseAsync(output, options, token);
        if (info.PrimaryVideoStream == null || info.PrimaryAudioStream == null || info.Duration <= TimeSpan.Zero)
            throw new InvalidDataException("Video output is missing graphics or audio.");
    }

    /// <summary>Use the source's average compressed bitrate without rounding upward; lossless or unknown inputs use the fallback.</summary>
    internal static async Task<int> ResolveAudioBitrateKbpsAsync(string audio, ConversionOptions settings,
        FFOptions options, CancellationToken token)
    {
        if (!settings.VideoAudioMatchSourceBitrate) return settings.VideoAudioBitrateKbps;
        var info = await MediaProbe.AnalyseAsync(audio, options, token);
        var stream = info.PrimaryAudioStream;
        if (stream == null) throw new InvalidDataException("Input has no audio stream.");
        double source = Convert.ToDouble(stream.BitRate, System.Globalization.CultureInfo.InvariantCulture) / 1000;
        return SelectAudioBitrateKbps(stream.CodecName, source, settings.VideoAudioBitrateKbps);
    }

    internal static int SelectAudioBitrateKbps(string? codecName, double sourceKbps, int fallbackKbps)
    {
        string codec = codecName?.ToLowerInvariant() ?? "";
        if (codec.StartsWith("pcm_", StringComparison.Ordinal) ||
            codec is "flac" or "alac" or "ape" or "wavpack" or "tta" or "shorten" or "wmalossless" or "truehd" or "mlp")
            return fallbackKbps;
        if (!double.IsFinite(sourceKbps) || sourceKbps < 32) return fallbackKbps;
        int[] supported = [32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320];
        return supported.Last(rate => rate <= Math.Min(sourceKbps, 320));
    }

    private static async Task<string> ChooseH264Async(string ffmpeg, CancellationToken token)
    {
        string encoders = await RunAsync(ffmpeg, ["-hide_banner", "-encoders"], Path.GetDirectoryName(ffmpeg)!, token, readStdout: true);
        if (encoders.Contains(" libopenh264 ", StringComparison.Ordinal)) return "libopenh264";
        if (encoders.Contains(" libx264 ", StringComparison.Ordinal)) return "libx264";
        // MPEG-4 Part 2 is available in standard FFmpeg builds and keeps MP4/MKV usable.
        return "mpeg4";
    }

    private static async Task<string> RunAsync(string executable, IEnumerable<string> arguments, string workingDirectory,
        CancellationToken token, bool readStdout = false)
    {
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = readStdout
        };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        token.ThrowIfCancellationRequested();
        using var process = Process.Start(start) ?? throw new IOException("Could not start bundled FFmpeg.");
        using var registration = token.Register(() => { try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { } });
        Task<string> errors = process.StandardError.ReadToEndAsync();
        Task<string>? output = readStdout ? process.StandardOutput.ReadToEndAsync() : null;
        await process.WaitForExitAsync();
        token.ThrowIfCancellationRequested();
        string error = await errors;
        if (process.ExitCode != 0)
            throw new InvalidDataException("FFmpeg video encoding failed: " + error.Trim()[..Math.Min(1200, error.Trim().Length)]);
        return output == null ? "" : await output;
    }
}
