using KaraokeToolkit.Core.Execution;
using KaraokeToolkit.Core.Video;
using KaraokeToolkit.Core.Audio;
using System.IO.Compression;
using System.Diagnostics;
using System.Globalization;
using FFMpegCore;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Recovery;
using Action = KaraokeToolkit.Core.Execution.OperationId;

namespace KaraokeToolkit.Tests.Video;

public class KaraokeVideoTests
{
    [Theory]
    [InlineData(VideoGraphicsSmoothing.None, VideoResolution.HD, 1280, 720)]
    [InlineData(VideoGraphicsSmoothing.Bilinear, VideoResolution.FullHD, 1920, 1080)]
    [InlineData(VideoGraphicsSmoothing.Bicubic, VideoResolution.QHD, 2560, 1440)]
    [InlineData(VideoGraphicsSmoothing.Lanczos, VideoResolution.UHD, 3840, 2160)]
    public async Task RendersSmoothingChoicesAtSelectedResolution(VideoGraphicsSmoothing smoothing,
        VideoResolution resolution, int width, int height)
    {
        string folder = TestMediaFixture.NewFolder();
        string graphics = Path.Combine(folder, "song.cdg"), audio = Path.Combine(folder, "song.wav");
        WriteWave(audio);
        WriteGraphics(graphics);
        byte[] originalGraphics = File.ReadAllBytes(graphics);
        string output = Path.Combine(folder, "song.mp4");
        await VideoEncoderService.EncodeAsync(graphics, audio, output,
            new() { VideoResolution = resolution, VideoGraphicsSmoothing = smoothing, VideoFrameRate = 25 }, default);
        var info = await MediaProbe.AnalyseAsync(output, FFmpegRuntime.Options(folder));
        Assert.Equal(width, info.PrimaryVideoStream!.Width);
        Assert.Equal(height, info.PrimaryVideoStream.Height);
        Assert.NotNull(info.PrimaryAudioStream);
        Assert.InRange(info.Duration.TotalSeconds, 0.9, 1.2);
        Assert.Equal(originalGraphics, File.ReadAllBytes(graphics));
    }

    [Theory]
    [InlineData("pair", VideoContainer.MP4, ".mp4")]
    [InlineData("zip", VideoContainer.MKV, ".mkv")]
    [InlineData("bin", VideoContainer.AVI, ".avi")]
    public async Task RendersAllKaraokeSourcesAndRetainsOriginals(string sourceKind, VideoContainer container, string extension)
    {
        string folder = TestMediaFixture.NewFolder();
        string wav = Path.Combine(folder, "song.wav"), cdg = Path.Combine(folder, "song.cdg");
        WriteWave(wav);
        WriteGraphics(cdg);
        if (sourceKind == "zip")
        {
            using (var archive = ZipFile.Open(Path.Combine(folder, "song.zip"), ZipArchiveMode.Create))
            {
                archive.CreateEntryFromFile(wav, "song.wav");
                archive.CreateEntryFromFile(cdg, "song.cdg");
            }
            File.Delete(wav);
            File.Delete(cdg);
        }
        else if (sourceKind == "bin")
        {
            string pcm = Path.Combine(folder, "audio.pcm");
            await AudioDecoderService.DecodeAsync(wav, pcm, default);
            await BinConverter.CombineAsync(pcm, cdg, Path.Combine(folder, "song.bin"), default);
            File.Delete(pcm);
            File.Delete(wav);
            File.Delete(cdg);
        }
        string[] originals = Directory.GetFiles(folder);
        var options = new ConversionOptions
        {
            VideoContainer = container, DeleteOldFiles = true, AutoCreateCueText = true, RecurseFolders = false
        };
        var result = await new ConversionService(new RecoveryStore(TestMediaFixture.NewFolder()))
            .RunAsync(new(Action.KaraokeToVideo, folder, options));
        Assert.True(result.Succeeded == 1, string.Join("; ", result.Files.Select(file => file.Message)));
        Assert.Equal(0, result.Failed);
        string video = Path.Combine(folder, "song" + extension);
        Assert.True(new FileInfo(video).Length > 0);
        Assert.All(originals, path => Assert.True(File.Exists(path)));
        Assert.Empty(Directory.GetFiles(folder, "*.cue"));
        Assert.Empty(Directory.GetFiles(folder, "*.txt"));
        var info = await MediaProbe.AnalyseAsync(video, FFmpegRuntime.Options(folder));
        Assert.NotNull(info.PrimaryVideoStream);
        Assert.NotNull(info.PrimaryAudioStream);
        Assert.Equal(1280, info.PrimaryVideoStream!.Width);
        Assert.Equal(720, info.PrimaryVideoStream.Height);
        Assert.InRange(info.Duration.TotalSeconds, 0.9, 1.2);
    }

    [Fact]
    public void SelectedAudioFileMapsToMatchingGraphics()
    {
        string folder = TestMediaFixture.NewFolder();
        string wav = Path.Combine(folder, "song.wav"), cdg = Path.Combine(folder, "song.cdg");
        WriteWave(wav);
        WriteGraphics(cdg);
        var batch = Assert.Single(InputSelection.Plan(Action.KaraokeToVideo, [wav], false));
        Assert.Equal(cdg, Assert.Single(batch.Sources));
    }

    [Fact]
    public async Task InvalidGraphicsDoesNotPublishVideoOrDeleteAudio()
    {
        string folder = TestMediaFixture.NewFolder();
        string wav = Path.Combine(folder, "song.wav"), cdg = Path.Combine(folder, "song.cdg");
        WriteWave(wav);
        File.WriteAllBytes(cdg, new byte[240]);
        var result = await new ConversionService(new RecoveryStore(TestMediaFixture.NewFolder()))
            .RunAsync(new(Action.KaraokeToVideo, folder, new ConversionOptions { DeleteOldFiles = true }));
        Assert.Equal(1, result.Failed);
        Assert.True(File.Exists(wav));
        Assert.True(File.Exists(cdg));
        Assert.False(File.Exists(Path.Combine(folder, "song.mp4")));
    }

    [Fact]
    public async Task LastGraphicsFramePersistsUntilAudioEnds()
    {
        string folder = TestMediaFixture.NewFolder();
        WriteWave(Path.Combine(folder, "song.wav"), seconds: 2);
        WriteGraphics(Path.Combine(folder, "song.cdg")); // One second of graphics.
        var result = await new ConversionService(new RecoveryStore(TestMediaFixture.NewFolder()))
            .RunAsync(new(Action.KaraokeToVideo, folder, new ConversionOptions()));
        Assert.True(result.Succeeded == 1, string.Join("; ", result.Files.Select(file => file.Message)));
        var info = await MediaProbe.AnalyseAsync(Path.Combine(folder, "song.mp4"), FFmpegRuntime.Options(folder));
        Assert.InRange(info.Duration.TotalSeconds, 1.9, 2.2);
    }

    [Fact]
    public async Task VideoAudioBitrateMatchesCompressedSourceAndFallsBackForLossless()
    {
        string folder = TestMediaFixture.NewFolder();
        string wav = Path.Combine(folder, "song.wav"), mp3 = Path.Combine(folder, "song.mp3");
        WriteWave(wav);
        await AudioEncoderService.EncodeAsync(wav, mp3, AudioEncoder.MP3, 96, default);
        var settings = new ConversionOptions { VideoAudioBitrateKbps = 192 };
        var ffmpeg = FFmpegRuntime.Options(folder);
        Assert.Equal(96, await VideoEncoderService.ResolveAudioBitrateKbpsAsync(mp3, settings, ffmpeg, default));
        Assert.Equal(192, await VideoEncoderService.ResolveAudioBitrateKbpsAsync(wav, settings, ffmpeg, default));
        Assert.Equal(192, await VideoEncoderService.ResolveAudioBitrateKbpsAsync(mp3,
            settings with { VideoAudioMatchSourceBitrate = false }, ffmpeg, default));
    }

    [Theory]
    [InlineData("mp3", 0, 192)]
    [InlineData("aac", 141, 128)]
    [InlineData("mp3", 500, 320)]
    [InlineData("flac", 900, 192)]
    public void UnknownAndLosslessBitratesUseFallbackWithoutOverbitrate(string codec, double sourceKbps, int expected) =>
        Assert.Equal(expected, VideoEncoderService.SelectAudioBitrateKbps(codec, sourceKbps, 192));

    [Fact]
    public async Task VideoHasFrequentKeyframesForSeeking()
    {
        string folder = TestMediaFixture.NewFolder();
        WriteWave(Path.Combine(folder, "song.wav"), seconds: 4);
        WriteGraphics(Path.Combine(folder, "song.cdg"));
        var result = await new ConversionService(new RecoveryStore(TestMediaFixture.NewFolder()))
            .RunAsync(new(Action.KaraokeToVideo, folder, new ConversionOptions()));
        Assert.True(result.Succeeded == 1, string.Join("; ", result.Files.Select(file => file.Message)));
        string probe = Path.Combine(FFmpegRuntime.Options(folder).BinaryFolder,
            OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe");
        var start = new ProcessStartInfo(probe)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string arg in new[] { "-v", "error", "-select_streams", "v:0", "-skip_frame", "nokey",
                     "-show_entries", "frame=best_effort_timestamp_time", "-of", "csv=p=0",
                     Path.Combine(folder, "song.mp4") })
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        string output = await process.StandardOutput.ReadToEndAsync();
        string errors = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, errors);
        double[] times = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => double.Parse(line, CultureInfo.InvariantCulture)).ToArray();
        Assert.InRange(times.Length, 4, 5);
        Assert.Equal(0, times[0]);
        for (int i = 1; i < times.Length; i++) Assert.InRange(times[i] - times[i - 1], 0.9, 1.1);
    }

    private static void WriteGraphics(string path)
    {
        byte[] bytes = new byte[300 * 24];
        for (int packet = 0; packet < 300; packet++)
        {
            int offset = packet * 24;
            bytes[offset] = 9; // CD+G command
            bytes[offset + 1] = 1; // Memory preset creates a renderable frame
            bytes[offset + 4] = (byte)(packet < 150 ? 0 : 1);
        }
        File.WriteAllBytes(path, bytes);
    }

    private static void WriteWave(string path, int seconds = 1)
    {
        const int rate = 44100;
        int samples = rate * seconds;
        using var output = File.Create(path);
        using var writer = new BinaryWriter(output);
        writer.Write("RIFF"u8);
        writer.Write(36 + samples * 4);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)2);
        writer.Write(rate);
        writer.Write(rate * 4);
        writer.Write((short)4);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(samples * 4);
        for (int i = 0; i < samples; i++)
        {
            short value = (short)(10000 * Math.Sin(i * 2 * Math.PI * 440 / rate));
            writer.Write(value);
            writer.Write(value);
        }
    }
}
