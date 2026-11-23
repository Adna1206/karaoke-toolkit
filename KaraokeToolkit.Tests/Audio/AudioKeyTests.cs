using System.Buffers.Binary;
using System.IO.Compression;
using FFMpegCore;
using KaraokeToolkit.Core.Archives;
using KaraokeToolkit.Core.Audio;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Execution;
using KaraokeToolkit.Core.Recovery;

namespace KaraokeToolkit.Tests.Audio;

public class AudioKeyTests
{
    private const int SampleRate = 44100;
    private static readonly double[] CueTimes = [0.5, 3.2, 6.0];

    [Theory]
    [InlineData(-2, false)]
    [InlineData(3, true)]
    public async Task ShiftsPitchButKeepsDurationAndCueTiming(int shift, bool zipped)
    {
        string sourceFolder = TestMediaFixture.NewFolder();
        string output = TestMediaFixture.NewFolder();
        string wav = Path.Combine(sourceFolder, "ABC-001.wav");
        WriteCueTrack(wav);
        byte[] graphics = new byte[24 * 300 * 8];
        graphics[24 * 300 * 3] = 9;
        string source = wav;
        if (zipped)
        {
            source = Path.ChangeExtension(wav, ".zip");
            using var zip = ZipFile.Open(source, ZipArchiveMode.Create);
            zip.CreateEntryFromFile(wav, "tracks/ABC-001.wav");
            using var entry = zip.CreateEntry("tracks/ABC-001.cdg").Open();
            entry.Write(graphics);
        }
        else File.WriteAllBytes(Path.ChangeExtension(wav, ".cdg"), graphics);
        byte[] original = File.ReadAllBytes(source);
        var request = new OperationRequest(OperationId.ChangeAudioKey, [source])
        {
            OutputFolder = output, AudioKey = new() { Semitones = shift }
        };
        var preview = await new OperationPlanner().PlanAsync(request);
        var row = Assert.Single(preview.AudioKeyPreview!);
        Assert.Equal(AudioGainStatus.Ready, row.Status);
        Assert.Equal(shift, row.Semitones);
        Assert.Null(row.GainDb);
        Assert.Empty(Directory.EnumerateFileSystemEntries(output));

        var service = new OperationExecutionService(new RecoveryStore(TestMediaFixture.NewFolder()));
        var result = Assert.Single((await service.ExecuteAsync(request)).Report.Entries);
        Assert.True(result.Status == "Succeeded", result.Message);
        Assert.Contains(FormattableString.Invariant($"{shift:+0;-0;0} semitones"), result.Message);
        Assert.Equal(original, File.ReadAllBytes(source));
        string adjusted = result.Outputs[0];
        if (zipped)
        {
            Assert.Single(result.Outputs);
            using var zip = ZipFile.OpenRead(adjusted);
            using var entry = zip.GetEntry("tracks/ABC-001.cdg")!.Open();
            using var bytes = new MemoryStream();
            entry.CopyTo(bytes);
            Assert.Equal(graphics, bytes.ToArray());
            adjusted = (await AudioGainArchive.ExtractAudioAsync(adjusted, TestMediaFixture.NewFolder(), default)).Path;
        }
        else Assert.Equal(graphics, File.ReadAllBytes(result.Outputs[1]));

        string pcm = Path.Combine(TestMediaFixture.NewFolder(), "adjusted.pcm");
        await AudioDecoderService.DecodeAsync(adjusted, pcm, default);
        byte[] audio = File.ReadAllBytes(pcm);
        Assert.Equal(8 * SampleRate * 4, audio.Length);
        short Sample(int frame) => BinaryPrimitives.ReadInt16LittleEndian(audio.AsSpan(frame * 4, 2));

        int periods = 0;
        for (int frame = (int)(3.35 * SampleRate); frame < (int)(3.75 * SampleRate); frame++)
            if (Sample(frame - 1) <= 0 && Sample(frame) > 0) periods++;
        double expectedFrequency = 440 * Math.Pow(2, shift / 12.0);
        Assert.InRange(periods / 0.4, expectedFrequency - 4, expectedFrequency + 4);
        foreach (double cue in CueTimes)
        {
            int first = Enumerable.Range((int)((cue - 0.12) * SampleRate), (int)(0.24 * SampleRate))
                .First(frame => Math.Abs((int)Sample(frame)) > 2000);
            Assert.InRange((double)first / SampleRate, cue - 0.045, cue + 0.045);
        }
    }

    [Fact]
    public async Task LossyOutputCanRetainPeaksAboveTheGainToolsDefaultCeiling()
    {
        string folder = TestMediaFixture.NewFolder();
        string wav = Path.Combine(folder, "ABC-001.wav"), mp3 = Path.ChangeExtension(wav, ".mp3");
        WriteCueTrack(wav, amplitude: 32700);
        await FFMpegArguments.FromFileInput(wav)
            .OutputToFile(mp3, false, options => options.WithCustomArgument("-c:a libmp3lame -b:a 192k"))
            .ProcessAsynchronously(true, FFmpegRuntime.Options(folder));
        var service = new AudioGainService(new RecoveryStore(TestMediaFixture.NewFolder()));
        var scan = await service.ScanAsync([mp3], false);
        string destination = TestMediaFixture.NewFolder();
        var plan = service.PlanKey(scan, new() { Semitones = 2 }, destination, OverwritePolicy.Fail);
        var result = Assert.Single((await service.ApplyAsync(plan)).Files);
        Assert.True(result.Status == ConversionStatus.Succeeded, result.Message);
        Assert.Equal("Key shift +2 semitones.", result.Message);
        var actual = await AudioGainMedia.MeasureAsync(Assert.Single(result.Outputs), default);
        Assert.True(actual.PeakDbtp > -0.9);
        Assert.InRange(actual.DurationUs, 7_920_000, 8_080_000);
    }

    [Fact]
    public async Task RecoveryRetainsTheApprovedKeyShift()
    {
        string folder = TestMediaFixture.NewFolder(), output = TestMediaFixture.NewFolder();
        string first = Path.Combine(folder, "ABC-001.wav"), second = Path.Combine(folder, "ABC-002.wav");
        WriteCueTrack(first);
        File.Copy(first, second);
        var recovery = new RecoveryStore(TestMediaFixture.NewFolder());
        var service = new AudioGainService(recovery);
        var scan = await service.ScanAsync([first, second], false);
        var plan = service.PlanKey(scan, new() { Semitones = -3 }, output, OverwritePolicy.Fail);
        var progress = new SynchronousProgress<ConversionProgress>(value =>
        {
            if (value.ActivitySource == second && value.ActivityLabel == "Processing") throw new IOException("Worker interrupted.");
        });
        await Assert.ThrowsAsync<IOException>(() => service.ApplyAsync(plan, progress));
        var job = Assert.Single(recovery.Find());
        Assert.Equal("Change Audio Key", job.Action);
        Assert.Equal(1, job.Completed);
        var result = await new OperationExecutionService(recovery).ResumeAsync(job.JournalPath);
        Assert.False(result.HasIssues);
        Assert.Equal(2, result.Report.Entries.Count);
        Assert.All(result.Report.Entries, entry => Assert.Contains("-3 semitones", entry.Message));
        Assert.Empty(recovery.Find());
    }

    private static void WriteCueTrack(string path, int amplitude = 16000)
    {
        const int frames = 8 * SampleRate;
        using var output = new BinaryWriter(File.Create(path));
        output.Write("RIFF"u8);
        output.Write(frames * 4 + 36);
        output.Write("WAVEfmt "u8);
        output.Write(16);
        output.Write((short)1);
        output.Write((short)2);
        output.Write(SampleRate);
        output.Write(SampleRate * 4);
        output.Write((short)4);
        output.Write((short)16);
        output.Write("data"u8);
        output.Write(frames * 4);
        for (int frame = 0; frame < frames; frame++)
        {
            double time = frame / (double)SampleRate;
            bool playing = CueTimes.Any(cue => time >= cue && time < cue + 0.7);
            short sample = playing ? (short)(amplitude * Math.Sin(2 * Math.PI * 440 * time)) : (short)0;
            output.Write(sample);
            output.Write(sample);
        }
    }
}
