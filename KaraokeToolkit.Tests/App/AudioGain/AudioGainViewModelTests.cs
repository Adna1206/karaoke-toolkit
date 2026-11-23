using KaraokeToolkit.App.Features.Main;
using KaraokeToolkit.Core.Audio;
using KaraokeToolkit.Tests.Benchmarks;

namespace KaraokeToolkit.Tests.App.AudioGain;

public class AudioGainViewModelTests
{
    [Fact]
    public async Task DesktopOpensTheSharedPreviewWithSemitoneControls()
    {
        string source = Path.Combine(TestMediaFixture.NewFolder(), "ABC-001.wav");
        AudioBenchmarks.WriteWave(source, 2);
        string output = TestMediaFixture.NewFolder();
        var model = new MainViewModel(Path.Combine(TestMediaFixture.NewFolder(), "options.xmls"));
        model.SelectedOperation = model.Operations.Single(operation => operation.IsAudioKey);
        model.SetInputs([source]);
        Assert.True(model.SelectedOperation.IsAudioKey);
        model.PickAudioGainDestinationAsync = () => Task.FromResult<string?>(output);
        model.ConfigureAudioGainAsync = async preview =>
        {
            Assert.True(preview.IsKeyChange);
            Assert.False(preview.IsLoudness);
            Assert.Equal("Apply Key Change", preview.ApplyLabel);
            await preview.ScanAsync(default);
            Assert.False(preview.CanApply);
            preview.Semitones = -2;
            Assert.True(preview.CanApply);
            Assert.Equal("-2 st", Assert.Single(preview.Rows).Change);
            return preview.Plan;
        };
        await model.StartCommand.ExecuteAsync(null);
        Assert.Equal("Audio Key Report", model.LastReport!.ReportType);
        Assert.Equal("Succeeded", Assert.Single(model.LastReport.Entries).Status);
        Assert.True(File.Exists(Path.Combine(output, "ABC-001.wav")));
    }

    [Fact]
    public async Task DesktopReusesMeasurementsAcrossModeChangesAndAppliesTheApprovedPlan()
    {
        string source = Path.Combine(TestMediaFixture.NewFolder(), "ABC-001.wav");
        AudioBenchmarks.WriteWave(source, 2);
        string output = TestMediaFixture.NewFolder();
        var model = new MainViewModel(Path.Combine(TestMediaFixture.NewFolder(), "options.xmls"));
        model.SelectedOperation = model.Operations.Single(operation => operation.IsAudioGain);
        Assert.False(model.SelectedOperation.IsValidation);
        Assert.Contains(".zip", model.SelectedOperation.SourceFileExtensions!);
        model.SetInputs([source]);
        Assert.True(model.SelectedOperation.IsAudioGain);
        model.PickAudioGainDestinationAsync = () => Task.FromResult<string?>(output);
        model.ConfigureAudioGainAsync = async preview =>
        {
            await preview.ScanAsync(default);
            var measurement = Assert.Single(preview.Rows).Row;
            preview.SelectedMode = preview.Modes.Single(mode => mode.Mode == AudioGainMode.Offset);
            preview.OffsetDb = -8;
            var row = Assert.Single(preview.Rows).Row;
            Assert.Equal(measurement.CurrentLufs, row.CurrentLufs);
            Assert.Equal(-8, row.GainDb);
            Assert.True(preview.CanApply);
            return preview.Plan;
        };
        await model.StartCommand.ExecuteAsync(null);
        var applied = Assert.Single(model.LastReport!.Entries);
        Assert.Equal("Succeeded", applied.Status);
        Assert.Contains("-8.00 dB", applied.Message);
        Assert.True(File.Exists(Path.Combine(output, "ABC-001.wav")));
    }
}
