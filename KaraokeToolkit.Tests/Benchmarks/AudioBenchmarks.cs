using KaraokeToolkit.Core.Audio;
using KaraokeToolkit.Core.Archives;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using FFMpegCore;
using KaraokeToolkit.Core.Conversion;
namespace KaraokeToolkit.Tests.Benchmarks;

/// <summary>Explicit opt-in wall-clock benchmarks. Never discovered as unit tests or used as a timing gate.</summary>
internal static class AudioBenchmarks
{
    private sealed record Measurement(string Operation, string Input, string Engine, double[] Milliseconds, double MedianMs, long OutputBytes, double? PcmSeconds, string Sha256);
    internal static async Task<int> RunAsync(string[] args)
    {
        int iterations = args.Length > 0 ? int.Parse(args[0]) : 3;
        if (iterations is < 1 or > 20) throw new ArgumentOutOfRangeException(nameof(iterations));
        string artifacts = typeof(AudioBenchmarks).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "RepositoryArtifactsPath").Value!;
        string folder = Path.GetFullPath(Path.Combine(artifacts, "benchmarks", "audio", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6]));
        Directory.CreateDirectory(folder);
        var results = new List<Measurement>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(20));
        var token = timeout.Token;
        foreach (int seconds in new[] { 2, 30 })
        {
            string wav = Path.Combine(folder, $"sample-{seconds}s.wav");
            WriteWave(wav, seconds);
            // Deterministic synthetic audio, suitable for comparing measurements across builds.
            var inputs = new List<string> { wav };
            foreach (var codec in new[] { (AudioEncoder.MP3, ".mp3"), (AudioEncoder.OGG, ".ogg") })
            {
                string encoded = Path.ChangeExtension(wav, codec.Item2);
                await AudioEncoderService.EncodeAsync(wav, encoded, codec.Item1, 192, token);
                inputs.Add(encoded);
            }
            string flac = Path.ChangeExtension(wav, ".flac");
            await FFMpegArguments.FromFileInput(wav).OutputToFile(flac, false, o => o.WithAudioCodec("flac"))
                .CancellableThrough(token).ProcessAsynchronously(true, FFmpegRuntime.Options(folder));
            inputs.Add(flac);
            foreach (string input in inputs)
            {
                results.Add(await Measure("Decode", Path.GetFileName(input), ".pcm",
                    path => AudioDecoderService.DecodeAsync(input, path, token), pcm: true));
            }
            foreach (AudioEncoder codec in new[] { AudioEncoder.MP3, AudioEncoder.OGG })
            {
                string ext = codec == AudioEncoder.MP3 ? ".mp3" : ".ogg";
                results.Add(await Measure("Encode + verify " + codec, Path.GetFileName(wav), ext,
                    path => AudioEncoderService.EncodeAsync(wav, path, codec, 192, token), pcm: false));
            }
            string bin = Path.ChangeExtension(wav, ".bin");
            string raw = Path.ChangeExtension(wav, ".pcm");
            await AudioDecoderService.DecodeAsync(wav, raw, token);
            string cdg = Path.ChangeExtension(wav, ".cdg");
            await File.WriteAllBytesAsync(cdg, new byte[seconds * 75 * 96], token);
            await BinConverter.CombineAsync(raw, cdg, bin, token);
            results.Add(await Measure("BIN to ZIP pipeline", Path.GetFileName(bin), ".zip", async path =>
            {
                string trackWav = Path.ChangeExtension(path, ".wav"), trackCdg = Path.ChangeExtension(path, ".cdg"), mp3 = Path.ChangeExtension(path, ".mp3");
                await BinConverter.SplitAsync(bin, trackWav, trackCdg, token);
                await AudioEncoderService.EncodeAsync(trackWav, mp3, AudioEncoder.MP3, 192, token);
                await KaraokeZipWriter.CreateAsync(mp3, trackCdg, path, ZipCompression.Balanced, token);
                KaraokeArchive.VerifyCreated(path, mp3, trackCdg);
                if (!File.ReadAllBytes(cdg).SequenceEqual(File.ReadAllBytes(trackCdg))) throw new InvalidDataException("CDG payload changed.");
            }, pcm: false));
        }
        var report = new { Timestamp = DateTimeOffset.UtcNow, OS = RuntimeInformation.OSDescription, Architecture = RuntimeInformation.ProcessArchitecture.ToString(), Runtime = RuntimeInformation.FrameworkDescription,
            Iterations = iterations, Warmups = 1, BitrateKbps = 192, Corpus = "Deterministic 2s and 30s stereo 44.1kHz multitone; shared WAV/MP3/OGG/FLAC inputs", Results = results };
        await File.WriteAllTextAsync(Path.Combine(folder, "results.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }), token);
        var lines = new List<string> { "# Audio codec benchmark", "", $"Run: {report.Timestamp:O}", $"OS: {report.OS}; {report.Architecture}; {report.Runtime}", $"{iterations} measured iterations after one warmup per case. Median wall-clock milliseconds; lower is faster.", "", "Includes process startup and validation. Encode uses 192 kbps. Pipeline measures split/encode/verify/ZIP stages, excluding GUI and publication. Synthetic corpus; no perceptual quality claim.", "", "| Operation | Input | FFmpeg ms | Output bytes |", "|---|---|---:|---:|" };
        foreach (var measurement in results)
        {
            lines.Add($"| {measurement.Operation} | {measurement.Input} | {measurement.MedianMs:F1} | {measurement.OutputBytes} |");
        }
        lines.Add("\nPCM sample durations, output sizes, hashes and individual timings are in results.json. Compare runs using the same machine and corpus. FFmpeg runtime-manifest.json identifies the bundled build.");
        await File.WriteAllLinesAsync(Path.Combine(folder, "results.md"), lines, token);
        Console.WriteLine(Path.Combine(folder, "results.md"));
        return 0;

        async Task<Measurement> Measure(string operation, string input, string extension, Func<string, Task> action, bool pcm)
        {
            const string engine = "FFmpeg";
            Console.WriteLine($"{operation}: {input} — {engine}");
            var samples = new List<double>(); string last = "";
            for (int i = 0; i <= iterations; i++)
            {
                last = Path.Combine(folder, Guid.NewGuid().ToString("N") + extension);
                var watch = Stopwatch.StartNew(); await action(last); watch.Stop();
                if (i > 0) samples.Add(watch.Elapsed.TotalMilliseconds);
            }
            var sorted = samples.Order().ToArray();
            double median = sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2;
            long size = new FileInfo(last).Length;
            using var stream = File.OpenRead(last);
            return new(operation, input, engine, samples.ToArray(), median, size, pcm ? size / 176400d : null, Convert.ToHexString(SHA256.HashData(stream)));
        }
    }
    internal static void WriteWave(string path, int seconds)
    {
        using var output = new BinaryWriter(File.Create(path));
        int frames = 44100 * seconds;
        output.Write("RIFF"u8); output.Write(frames * 4 + 36); output.Write("WAVEfmt "u8); output.Write(16); output.Write((short)1); output.Write((short)2);
        output.Write(44100); output.Write(176400); output.Write((short)4); output.Write((short)16); output.Write("data"u8); output.Write(frames * 4);
        for (int i = 0; i < frames; i++)
        {
            double t = i / 44100d;
            short sample = (short)(9000 * Math.Sin(t * 2 * Math.PI * 440) + 3500 * Math.Sin(t * 2 * Math.PI * 997));
            output.Write(sample); output.Write(sample);
        }
    }
}
