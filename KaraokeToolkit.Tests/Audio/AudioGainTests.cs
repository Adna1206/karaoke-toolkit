using FFMpegCore;
using System.IO.Compression;
using KaraokeToolkit.Core.Archives;
using KaraokeToolkit.Core.Audio;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Execution;
using KaraokeToolkit.Core.Recovery;
using KaraokeToolkit.Tests.Benchmarks;

namespace KaraokeToolkit.Tests.Audio;

public class AudioGainTests
{
    [Fact]
    public async Task KaraokeZipPreviewAndApplyPreserveMembersAndChangeOnlyAudio()
    {
        var fixture = new Fixture();
        string reference = Path.Combine(TestMediaFixture.NewFolder(), "reference.wav");
        AudioBenchmarks.WriteWave(reference, 2);
        string source = Path.Combine(fixture.Source, "ABC-001.zip");
        byte[] graphics = Enumerable.Range(0, 24).Select(value => (byte)value).ToArray();
        byte[] notes = "Karaoke notes"u8.ToArray();
        using (var zip = ZipFile.Open(source, ZipArchiveMode.Create))
        {
            zip.CreateEntry("extras/");
            zip.CreateEntry("extras/empty.txt");
            zip.CreateEntryFromFile(reference, "tracks/ABC-001.wav");
            WriteEntry(zip, "graphics/ABC-001.cdg", graphics);
            WriteEntry(zip, "extras/notes.txt", notes);
        }
        byte[] original = File.ReadAllBytes(source);
        var request = new OperationRequest(OperationId.NormalizeAudioGain, [fixture.Source])
        {
            OutputFolder = fixture.Output, AudioGain = Offset(-6)
        };
        var preview = await new OperationPlanner().PlanAsync(request);
        var row = Assert.Single(preview.AudioGainPreview!);
        Assert.Equal(AudioGainStatus.Ready, row.Status);
        Assert.Equal(source, row.Source);
        Assert.Null(row.GraphicsOutput);
        Assert.Equal(-6, row.GainDb);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.Output));

        var applied = await new OperationExecutionService(fixture.Recovery).ExecuteAsync(request);
        var result = Assert.Single(applied.Report.Entries);
        Assert.True(result.Status == "Succeeded", result.Message);
        string output = Assert.Single(result.Outputs);
        Assert.Equal(Path.Combine(fixture.Output, "ABC-001.zip"), output);
        Assert.Equal(original, File.ReadAllBytes(source));
        using (var zip = ZipFile.OpenRead(output))
        {
            Assert.Equal(new[] { "extras/", "extras/empty.txt", "extras/notes.txt", "graphics/ABC-001.cdg", "tracks/ABC-001.wav" },
                zip.Entries.Select(entry => entry.FullName).Order(StringComparer.Ordinal));
            Assert.Equal(graphics, ReadEntry(zip, "graphics/ABC-001.cdg"));
            Assert.Equal(notes, ReadEntry(zip, "extras/notes.txt"));
            Assert.Empty(ReadEntry(zip, "extras/empty.txt"));
        }
        var audio = await AudioGainArchive.ExtractAudioAsync(output, TestMediaFixture.NewFolder(), default);
        string dashFile = Path.Combine(Path.GetDirectoryName(audio.Path)!, "-");
        File.WriteAllText(dashFile, "existing");
        var actual = await AudioGainMedia.MeasureAsync(audio.Path, default);
        Assert.InRange(actual.Lufs!.Value - row.CurrentLufs!.Value, -6.05, -5.95);
        Assert.Equal("existing", File.ReadAllText(dashFile));
    }

    [Theory]
    [InlineData("tracks/ABC-002.wav", "one audio file")]
    [InlineData("../notes.txt", "Unsafe archive path")]
    public async Task KaraokeZipPreviewRejectsAmbiguousAudioAndUnsafeNames(string extra, string message)
    {
        var fixture = new Fixture();
        string wave = fixture.WriteWave("ABC-001");
        string source = Path.Combine(fixture.Source, "ABC-001.zip");
        using (var zip = ZipFile.Open(source, ZipArchiveMode.Create))
        {
            zip.CreateEntryFromFile(wave, "tracks/ABC-001.wav");
            WriteEntry(zip, "tracks/ABC-001.cdg", new byte[24]);
            zip.CreateEntry(extra);
        }
        var scan = await fixture.Service.ScanAsync([source], false);
        var plan = fixture.Service.Plan(scan, Offset(-3), fixture.Output, OverwritePolicy.Fail);
        var row = Assert.Single(plan.Rows);
        Assert.Equal(AudioGainStatus.Blocked, row.Status);
        Assert.Contains(message, row.Message);
        var result = Assert.Single((await fixture.Service.ApplyAsync(plan)).Files);
        Assert.Equal(ConversionStatus.Failed, result.Status);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.Output));
    }

    private static void WriteEntry(ZipArchive archive, string name, byte[] bytes)
    {
        using var entry = archive.CreateEntry(name).Open();
        entry.Write(bytes);
    }

    private static byte[] ReadEntry(ZipArchive archive, string name)
    {
        using var entry = archive.GetEntry(name)!.Open();
        using var contents = new MemoryStream();
        entry.CopyTo(contents);
        return contents.ToArray();
    }

    [Theory]
    [InlineData(".wav", "pcm_s16le")]
    [InlineData(".mp3", "libmp3lame")]
    [InlineData(".m4a", "aac")]
    [InlineData(".aac", "aac")]
    [InlineData(".flac", "flac")]
    [InlineData(".m4a", "alac")]
    [InlineData(".wma", "wmav2")]
    [InlineData(".ogg", "libvorbis")]
    public async Task OffsetWritesSameFormatAndPreservesOriginalsAndCdg(string extension, string encoder)
    {
        var fixture = new Fixture();
        string wave = fixture.WriteWave("reference");
        string source = Path.Combine(fixture.Source, "ABC-001" + extension);
        await FFMpegArguments.FromFileInput(wave)
            .OutputToFile(source, false, o => o.WithCustomArgument($"-c:a {encoder} -metadata title=GainTest"))
            .ProcessAsynchronously(true, FFmpegRuntime.Options(fixture.Source));
        string cdg = Path.ChangeExtension(source, ".cdg");
        File.WriteAllBytes(cdg, Enumerable.Range(0, 24).Select(value => (byte)value).ToArray());
        byte[] original = File.ReadAllBytes(source);
        var scan = await fixture.Service.ScanAsync([source], false);
        var plan = fixture.Service.Plan(scan, Offset(-6), fixture.Output, OverwritePolicy.Fail);
        var row = Assert.Single(plan.Rows);
        Assert.True(row.Status == AudioGainStatus.Ready, row.Message);
        Assert.Equal(-6, row.GainDb);
        Assert.Empty(Directory.GetFileSystemEntries(fixture.Output));
        var applied = await fixture.Service.ApplyAsync(plan);
        var result = Assert.Single(applied.Files);
        Assert.True(result.Status == ConversionStatus.Succeeded, result.Message);
        Assert.Equal([Path.Combine(fixture.Output, Path.GetFileName(source)), Path.Combine(fixture.Output, Path.GetFileName(cdg))], result.Outputs);
        var actual = await AudioGainMedia.MeasureAsync(result.Outputs[0], default);
        Assert.InRange(actual.Lufs!.Value - row.CurrentLufs!.Value, -6.8, -5.2);
        Assert.Equal(original, File.ReadAllBytes(source));
        Assert.Equal(File.ReadAllBytes(cdg), File.ReadAllBytes(result.Outputs[1]));
        var peak = fixture.Service.Plan(scan, new() { Mode = AudioGainMode.Peak, PeakCeilingDbtp = -3 },
            fixture.Output, OverwritePolicy.Replace);
        var peakResult = Assert.Single((await fixture.Service.ApplyAsync(peak)).Files);
        Assert.True(peakResult.Status == ConversionStatus.Succeeded, peakResult.Message);
        var peakActual = await AudioGainMedia.MeasureAsync(peakResult.Outputs[0], default);
        Assert.InRange(peakActual.PeakDbtp!.Value, -3.8, -2.95);
        Assert.Equal(original, File.ReadAllBytes(source));
        Assert.Empty(fixture.Recovery.Find());
    }

    [Fact]
    public async Task LoudnessAndPeakModesUseDifferentMeasurementsAndApplyTheCeiling()
    {
        var fixture = new Fixture();
        string source = fixture.WriteWave("ABC-001");
        var scan = await fixture.Service.ScanAsync([source], false);
        var options = new AudioGainOptions();
        var loudness = fixture.Service.Plan(scan, options with { TargetLufs = -26 }, fixture.Output, OverwritePolicy.Fail);
        var row = Assert.Single(loudness.Rows);
        Assert.Equal(AudioGainStatus.Ready, row.Status);
        Assert.Equal(-26, row.EstimatedLufs!.Value, 5);
        var result = Assert.Single((await fixture.Service.ApplyAsync(loudness)).Files);
        Assert.True(result.Status == ConversionStatus.Succeeded, result.Message);
        var actual = await AudioGainMedia.MeasureAsync(result.Outputs[0], default);
        Assert.InRange(actual.Lufs!.Value, -26.1, -25.9);
        var limited = Assert.Single(fixture.Service.Plan(scan, options with { TargetLufs = -5, PeakCeilingDbtp = -8 },
            fixture.Output, OverwritePolicy.Replace).Rows);
        Assert.Equal(AudioGainStatus.Limited, limited.Status);
        Assert.Equal(-8, limited.EstimatedPeakDbtp!.Value, 5);
        var peak = fixture.Service.Plan(scan, options with { Mode = AudioGainMode.Peak, PeakCeilingDbtp = -8 },
            fixture.Output, OverwritePolicy.Replace);
        Assert.Equal(limited.GainDb, Assert.Single(peak.Rows).GainDb);
        var peakResult = Assert.Single((await fixture.Service.ApplyAsync(peak)).Files);
        Assert.True(peakResult.Status == ConversionStatus.Succeeded, peakResult.Message);
        var peakActual = await AudioGainMedia.MeasureAsync(peakResult.Outputs[0], default);
        Assert.InRange(peakActual.PeakDbtp!.Value, -8.03, -7.97);
        var clipped = Assert.Single(fixture.Service.Plan(scan, Offset(60), fixture.Output, OverwritePolicy.Replace).Rows);
        Assert.Equal(AudioGainStatus.Skipped, clipped.Status);
        Assert.Null(clipped.GainDb);
    }

    [Fact]
    public async Task FrozenPlanRejectsChangedAudioAndContinuesTheBatch()
    {
        var fixture = new Fixture();
        string first = fixture.WriteWave("ABC-001");
        string second = fixture.WriteWave("ABC-002");
        var scan = await fixture.Service.ScanAsync([fixture.Source], true);
        var plan = fixture.Service.Plan(scan, Offset(-3), fixture.Output, OverwritePolicy.Fail);
        File.AppendAllText(first, "changed");
        var result = await fixture.Service.ApplyAsync(plan);
        Assert.Equal(1, result.Failed);
        Assert.Equal(1, result.Succeeded);
        Assert.Contains("changed", result.Files[0].Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(fixture.Output, Path.GetFileName(first))));
        Assert.True(File.Exists(Path.Combine(fixture.Output, Path.GetFileName(second))));
    }

    [Fact]
    public async Task DryRunBlocksCollisionsAndNeverCreatesARecoveryWorkspace()
    {
        var fixture = new Fixture();
        string first = fixture.WriteWave("ABC-001");
        string nested = Directory.CreateDirectory(Path.Combine(fixture.Source, "nested")).FullName;
        File.Copy(first, Path.Combine(nested, Path.GetFileName(first)));
        var result = await new OperationPlanner().PlanAsync(new(OperationId.NormalizeAudioGain, [fixture.Source])
        {
            AudioGain = Offset(-3), OutputFolder = fixture.Output
        });
        Assert.True(result.HasIssues);
        Assert.Equal(2, result.Found);
        Assert.All(result.AudioGainPreview!, row => Assert.Equal(AudioGainStatus.Blocked, row.Status));
        Assert.Empty(Directory.GetFileSystemEntries(fixture.Output));
        Assert.Empty(fixture.Recovery.Find());
    }

    [Fact]
    public async Task ResumeUsesTheSavedGainAndKeepsPublishedFiles()
    {
        var fixture = new Fixture();
        string first = fixture.WriteWave("ABC-001");
        string second = fixture.WriteWave("ABC-002");
        var scan = await fixture.Service.ScanAsync([first, second], false);
        var plan = fixture.Service.Plan(scan, Offset(-3), fixture.Output, OverwritePolicy.Fail);
        var progress = new SynchronousProgress<ConversionProgress>(item =>
        {
            if (item.ActivitySource == second && item.ActivityLabel == "Processing") throw new IOException("Worker interrupted.");
        });
        await Assert.ThrowsAsync<IOException>(() => fixture.Service.ApplyAsync(plan, progress));

        var job = Assert.Single(fixture.Recovery.Find());
        Assert.Equal(RecoveryKind.AudioGain, job.Kind);
        Assert.Equal(1, job.Completed);
        byte[] firstCopy = File.ReadAllBytes(Path.Combine(fixture.Output, Path.GetFileName(first)));
        var result = await new OperationExecutionService(fixture.Recovery).ResumeAsync(job.JournalPath);
        Assert.False(result.HasIssues);
        Assert.Equal(2, result.Report.Entries.Count);
        Assert.Equal(firstCopy, File.ReadAllBytes(Path.Combine(fixture.Output, Path.GetFileName(first))));
        Assert.Equal(firstCopy, File.ReadAllBytes(Path.Combine(fixture.Output, Path.GetFileName(second))));
        Assert.Empty(fixture.Recovery.Find());
    }

    private static AudioGainOptions Offset(double value) => new() { Mode = AudioGainMode.Offset, OffsetDb = value };

    private sealed class Fixture
    {
        internal string Source { get; } = TestMediaFixture.NewFolder();
        internal string Output { get; } = TestMediaFixture.NewFolder();
        internal RecoveryStore Recovery { get; } = new(TestMediaFixture.NewFolder());
        internal AudioGainService Service => new(Recovery);
        internal string WriteWave(string name)
        {
            string path = Path.Combine(Source, name + ".wav");
            AudioBenchmarks.WriteWave(path, 2);
            return path;
        }
    }
}
