using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using FFMpegCore;

namespace KaraokeToolkit.Core.Audio;

internal sealed record MediaStreamInfo(string CodecName, int SampleRateHz, int Channels, long BitRate, int Width, int Height);
internal sealed record MediaInfo(TimeSpan Duration, MediaStreamInfo? PrimaryAudioStream, MediaStreamInfo? PrimaryVideoStream);

internal static class MediaProbe
{
    internal static async Task<MediaInfo> AnalyseAsync(string path, FFOptions options, CancellationToken token = default)
    {
        const int attempts = 2;
        for (int attempt = 1; ; attempt++)
        {
            string output = await ReadProbeOutputAsync(path, options, token);
            token.ThrowIfCancellationRequested();
            try
            {
                using var json = JsonDocument.Parse(output);
                return ReadMediaInfo(json.RootElement);
            }
            catch (JsonException) when (attempt < attempts)
            {
                // FFprobe can exit successfully with incomplete JSON. Retry the read-only probe once.
            }
            catch (JsonException error)
            {
                throw new InvalidDataException($"FFprobe returned invalid JSON for '{Path.GetFileName(path)}' after {attempts} attempts.", error);
            }
        }
    }

    private static async Task<string> ReadProbeOutputAsync(string path, FFOptions options, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string executable = Path.Combine(options.BinaryFolder, OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe");
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = options.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string arg in new[] { "-v", "error", "-protocol_whitelist", "file,pipe", "-of", "json",
            "-show_entries", "format=duration:stream=codec_type,codec_name,sample_rate,channels,bit_rate,width,height",
            "-i", FFmpegRuntime.LocalPath(path) })
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new IOException("Could not start bundled FFprobe.");
        using var registration = token.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { } // The process exited before it could be killed.
            catch (System.ComponentModel.Win32Exception) { } // Cancellation still ends the wait if termination fails.
        });
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(token);
        await Task.WhenAll(stdout, stderr).WaitAsync(token);
        token.ThrowIfCancellationRequested();
        if (process.ExitCode != 0)
        {
            string error = (await stderr).Trim();
            throw new InvalidDataException($"FFprobe failed for '{Path.GetFileName(path)}' (exit {process.ExitCode}): {error[..Math.Min(error.Length, 1200)]}");
        }

        return await stdout;
    }

    private static MediaInfo ReadMediaInfo(JsonElement root)
    {
        double seconds = root.TryGetProperty("format", out var format) &&
            double.TryParse(Text(format, "duration"), NumberStyles.Float, CultureInfo.InvariantCulture, out double duration)
            ? duration : 0;
        var length = double.IsFinite(seconds) && seconds > 0 && seconds < TimeSpan.MaxValue.TotalSeconds
            ? TimeSpan.FromSeconds(seconds) : TimeSpan.Zero;
        MediaStreamInfo? audio = null, video = null;
        if (root.TryGetProperty("streams", out var streams))
            foreach (var stream in streams.EnumerateArray())
            {
                string type = Text(stream, "codec_type");
                if (type == "audio") audio ??= ReadStream(stream);
                else if (type == "video") video ??= ReadStream(stream);
            }
        return new(length, audio, video);
    }

    private static MediaStreamInfo ReadStream(JsonElement stream) => new(
        Text(stream, "codec_name"), (int)Number(stream, "sample_rate"), (int)Number(stream, "channels"),
        Number(stream, "bit_rate"), (int)Number(stream, "width"), (int)Number(stream, "height"));

    private static string Text(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) ? value.ToString() : "";

    private static long Number(JsonElement item, string name) =>
        long.TryParse(Text(item, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out long value) ? value : 0;
}
