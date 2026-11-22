using KaraokeToolkit.App.Features.Main;
using KaraokeToolkit.App.Metadata;
using KaraokeToolkit.Core.Conversion;

namespace KaraokeToolkit.Tests.App.Main;

public class RunStatusTests
{
    [Fact]
    public async Task CheckingTheSavedRecoveryIndexShowsFooterProgress()
    {
        var model = new MainViewModel(Path.Combine(TestMediaFixture.NewFolder(), "options.xmls"));
        var footerProgress = new List<bool>();
        model.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(MainViewModel.ShowFooterProgress))
                footerProgress.Add(model.ShowFooterProgress);
        };

        await model.CheckRecoveryCommand.ExecuteAsync(null);

        Assert.Equal(new[] { true, false }, footerProgress);
    }

    [Theory]
    [InlineData(1, 0, 0, false, RunStatus.Success)]
    [InlineData(1, 1, 0, false, RunStatus.Warning)]
    [InlineData(1, 0, 1, false, RunStatus.Failure)]
    [InlineData(0, 0, 1, false, RunStatus.Failure)]
    [InlineData(0, 1, 0, false, RunStatus.Warning)]
    [InlineData(0, 0, 0, false, RunStatus.Warning)]
    [InlineData(1, 0, 1, true, RunStatus.Failure)]
    [InlineData(1, 0, 0, true, RunStatus.Neutral)]
    public void CompletionDistinguishesSuccessPartialFailureAndCancellation(int succeeded, int skipped, int failed, bool cancelled, RunStatus expected)
    {
        var files = Enumerable.Repeat(ConversionStatus.Succeeded, succeeded)
            .Concat(Enumerable.Repeat(ConversionStatus.Skipped, skipped))
            .Concat(Enumerable.Repeat(ConversionStatus.Failed, failed))
            .Select(status => new ConversionFileResult("source", status, [], "result")).ToArray();
        Assert.Equal(expected, RunStatusSummary.From(new(files, cancelled)));
    }

    [Fact]
    public async Task ValidationOutcomeResetsOnReturningHomeAndKeepsReport()
    {
        string folder = TestMediaFixture.NewFolder();
        var model = new MainViewModel(Path.Combine(folder, "settings.xml"));
        model.SelectedOperation = model.Operations.Single(operation => operation.IsValidation);
        model.SetInputs([folder]);
        await model.StartCommand.ExecuteAsync(null);
        Assert.True(model.ShowProgress);
        Assert.Equal(RunStatus.Warning, model.StatusOutcome);
        Assert.Equal(KaraokeToolkit.App.Shared.StatusKind.Warning, model.FooterStatusKind);
        var report = model.LastReport;
        Assert.NotNull(report);
        model.ShowProgress = false;
        Assert.Equal(RunStatus.Neutral, model.StatusOutcome);
        Assert.Contains("Choose an operation", model.Status);
        Assert.Same(report, model.LastReport);
        model.StatusOutcome = RunStatus.Failure;
        model.IsBusy = true;
        Assert.Equal(RunStatus.Neutral, model.StatusOutcome);
    }
}
