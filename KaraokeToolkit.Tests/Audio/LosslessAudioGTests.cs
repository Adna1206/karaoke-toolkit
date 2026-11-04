using KaraokeToolkit.Core.Audio;
using KaraokeToolkit.App.Features.Main;
using FFMpegCore;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Recovery;
using KaraokeToolkit.Core.Settings;
using KaraokeToolkit.Tests.Benchmarks;
using Action = KaraokeToolkit.Core.Conversion.OperationCatalog.defAction;

namespace KaraokeToolkit.Tests.Audio;

public class LosslessAudioGTests
{
    private static ConversionOptions Options(bool delete = false) => new()
    {
        AudioBitrate = 192, AutoCreateCueText = false, DeleteOldFiles = delete, RecurseFolders = false
    };
    private static ConversionService Service() => new(new RecoveryStore(TestMediaFixture.NewFolder()));

    [Fact]
    public async Task GuiMp3TranscodeUsesRememberedMp3BitrateWhenWmaIsSelected()
    {
        string folder = TestMediaFixture.NewFolder();
        AudioBenchmarks.WriteWave(Path.Combine(folder, "song.wav"), 2);
        File.WriteAllBytes(Path.Combine(folder, "song.cdg"), new byte[24 * 300]);
        var model = new MainViewModel(Path.Combine(TestMediaFixture.NewFolder(), "options.xmls"));
        model.SaveOptions(model.Options with { AudioEncoder = AudioEncoder.WMA, AudioBitrate = 160,
            DeleteOldFiles = false, AutoCreateCueText = false }, model.OverwritePolicy, model.Theme,
            model.ValidateBeforeConversion, model.IgnoreLeadingTheForFileOrganization,
            new AudioBitratePreferences(192, 128, 160));
        model.SelectedOperation = model.Operations.Single(x => x.Action == Action.LosslessG_To_Mp3G);
        model.SetInputs([folder]);
        await model.StartCommand.ExecuteAsync(null);
        Assert.Equal("Succeeded", Assert.Single(model.LastReport!.Entries).Status);
        var info = await FFProbe.AnalyseAsync(Path.Combine(folder, "song.mp3"), FFmpegRuntime.Options(folder));
        Assert.Equal(192000, info.PrimaryAudioStream?.BitRate);
    }

    [Theory]
    [InlineData("wav")]
    [InlineData("flac")]
    [InlineData("alac")]
    public async Task TranscodesLosslessPairAndPreservesGraphics(string format)
    {
        string folder = TestMediaFixture.NewFolder();
        string wav = Path.Combine(folder, "song.wav"), cdg = Path.Combine(folder, "song.cdg");
        AudioBenchmarks.WriteWave(wav, 2);
        byte[] graphics = new byte[24 * 300];
        Random.Shared.NextBytes(graphics);
        File.WriteAllBytes(cdg, graphics);
        string input = wav;
        if (format is "flac" or "alac")
        {
            input = Path.ChangeExtension(wav, format == "alac" ? ".m4a" : ".flac");
            await FFMpegArguments.FromFileInput(wav).OutputToFile(input, false, o => o.WithAudioCodec(format))
                .ProcessAsynchronously(true, FFmpegRuntime.Options(folder));
            File.Delete(wav);
        }

        var result = await Service().RunAsync(new(Action.LosslessG_To_Mp3G, folder, Options(delete: true)));
        Assert.True(result.Succeeded == 1, string.Join("; ", result.Files.Select(f => f.Message)));
        Assert.False(File.Exists(input));
        Assert.Equal(graphics, File.ReadAllBytes(cdg));
        string mp3 = Path.Combine(folder, "song.mp3");
        Assert.True(new FileInfo(mp3).Length > 0);
        var info = await FFProbe.AnalyseAsync(mp3, FFmpegRuntime.Options(folder));
        Assert.Equal("mp3", info.PrimaryAudioStream?.CodecName);
        Assert.Empty(Directory.GetDirectories(folder, ".karaoke-job-*"));
    }

    [Theory]
    [InlineData(22050)]
    [InlineData(48000)]
    public async Task ResamplesLowerAndHigherSourceRatesWithoutChangingCdgTiming(int sourceRate)
    {
        string folder = TestMediaFixture.NewFolder();
        string reference = Path.Combine(folder, "reference.wav"), input = Path.Combine(folder, "song.wav");
        AudioBenchmarks.WriteWave(reference, 2);
        await FFMpegArguments.FromFileInput(reference).OutputToFile(input, false,
            o => o.WithCustomArgument($"-ar {sourceRate} -ac 2 -c:a pcm_s16le"))
            .ProcessAsynchronously(true, FFmpegRuntime.Options(folder));
        File.Delete(reference);
        byte[] graphics = new byte[24 * 600];
        Random.Shared.NextBytes(graphics);
        string cdg = Path.Combine(folder, "song.cdg");
        File.WriteAllBytes(cdg, graphics);

        var result = await Service().RunAsync(new(Action.LosslessG_To_Mp3G, folder, Options(delete: true)));
        Assert.True(result.Succeeded == 1, string.Join("; ", result.Files.Select(f => f.Message)));
        var info = await FFProbe.AnalyseAsync(Path.Combine(folder, "song.mp3"), FFmpegRuntime.Options(folder));
        Assert.Equal(44100, info.PrimaryAudioStream?.SampleRateHz);
        Assert.Equal(graphics, File.ReadAllBytes(cdg));
        Assert.False(File.Exists(input));
    }

    [Fact]
    public async Task LossyM4aCannotPassAsLossless()
    {
        string folder = TestMediaFixture.NewFolder();
        string wav = Path.Combine(folder, "song.wav"), m4a = Path.Combine(folder, "song.m4a");
        AudioBenchmarks.WriteWave(wav, 2);
        await FFMpegArguments.FromFileInput(wav).OutputToFile(m4a, false, o => o.WithAudioCodec("aac"))
            .ProcessAsynchronously(true, FFmpegRuntime.Options(folder));
        File.Delete(wav);
        File.WriteAllBytes(Path.Combine(folder, "song.cdg"), new byte[24]);
        var result = await Service().RunAsync(new(Action.LosslessG_To_Mp3G, folder, Options(delete: true)));
        Assert.Equal(1, result.Failed);
        Assert.Contains("not a supported lossless", result.Files[0].Message);
        Assert.True(File.Exists(m4a));
        Assert.False(File.Exists(Path.Combine(folder, "song.mp3")));
    }

    [Fact]
    public async Task ExistingMp3AndAmbiguousSourcesLeavePairUntouched()
    {
        string folder = TestMediaFixture.NewFolder();
        string wav = Path.Combine(folder, "song.wav"), cdg = Path.Combine(folder, "song.cdg"), mp3 = Path.Combine(folder, "song.mp3");
        AudioBenchmarks.WriteWave(wav, 2);
        File.WriteAllBytes(cdg, new byte[24]);
        File.WriteAllText(mp3, "existing");
        var blocked = await Service().RunAsync(new(Action.LosslessG_To_Mp3G, folder, Options(delete: true)));
        Assert.Equal(1, blocked.Failed);
        Assert.Equal("existing", File.ReadAllText(mp3));
        Assert.True(File.Exists(wav));
        string flac = Path.ChangeExtension(wav, ".flac");
        File.WriteAllText(flac, "second source");
        var ambiguous = await Service().RunAsync(new(Action.LosslessG_To_Mp3G, folder, Options()));
        Assert.Equal(1, ambiguous.Failed);
        Assert.Contains("exactly one", ambiguous.Files[0].Message);
    }
}
