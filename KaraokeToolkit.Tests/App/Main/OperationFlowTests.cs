using KaraokeToolkit.Core.Execution;
using KaraokeToolkit.App.Features.Main;
using KaraokeToolkit.App.Features.TrackGrouping;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Recovery;
using KaraokeToolkit.Core.Settings;
using KaraokeToolkit.Core;
using Action = KaraokeToolkit.Core.Execution.OperationId;

namespace KaraokeToolkit.Tests.App.Main;

public class OperationFlowTests
{
    private static string Settings() => Path.Combine(TestMediaFixture.NewFolder(), "options.xmls");

    [Theory]
    [InlineData(613.5, "00:10:14")]
    [InlineData(86400.1, "24:00:01")]
    public void RemainingTimeUsesTheElapsedClockFormat(double seconds, string expected)
    {
        var model = new MainViewModel(Settings());

        model.UpdateActivityEstimate(new ProcessingEstimate(seconds, 2));
        Assert.Equal(expected, model.RemainingTime);
        model.UpdateActivityEstimate(null);
        Assert.Equal("—", model.RemainingTime);
    }

    [Fact]
    public void ActivityCountShowsDiscoveryThenCompletedItemsWithoutAddingRows()
    {
        var model = new MainViewModel(Settings());
        var progress = new ConversionProgress(0, 0, 1234, 0, "Finding items", "00:00:00")
            { ShowInActivity = false, IsIndeterminate = true };

        model.Report(progress);
        Assert.Equal($"{1234:N0} items found", model.ActivityItemCount);
        model.Report(progress with { IsIndeterminate = false });
        Assert.Equal($"0 of {1234:N0} items", model.ActivityItemCount);
        model.Report(progress with { IsIndeterminate = false, Completed = 7 });
        Assert.Equal($"7 of {1234:N0} items", model.ActivityItemCount);
        Assert.Empty(model.Activity);
    }

    [Fact]
    public async Task CancelledConversionKeepsTheCompletedCountAndOriginalTotal()
    {
        string folder = TestMediaFixture.NewFolder();
        TestMediaFixture.Seed(folder, Action.ZipToBin, "keep");
        string first = Path.Combine(folder, "ABC-01.zip");
        File.Move(Path.Combine(folder, "song.zip"), first);
        File.Copy(first, Path.Combine(folder, "ABC-02.zip"));
        string conflict = Path.Combine(folder, "ABC-02.wav");
        File.WriteAllText(conflict, "existing");
        var model = new MainViewModel(Settings());
        model.SelectedOperation = model.Operations.Single(operation => operation.Action == Action.ZipToAudioG);
        model.SaveOptions(model.Options with { AutoCreateCueText = false, DeleteOldFiles = false }, OverwritePolicy.Ask, model.Theme);
        model.ResolveConflictAsync = (_, _) => Task.FromResult(ConflictDecision.Cancel);
        model.SetInputs([folder]);

        await model.StartCommand.ExecuteAsync(null);

        Assert.True(model.LastReport!.Cancelled);
        Assert.Equal("1 of 2 items", model.ActivityItemCount);
        Assert.Equal("existing", File.ReadAllText(conflict));
    }

    [Theory]
    [InlineData(false, "2 of 2 items")]
    [InlineData(true, "1 of 1 item")]
    public async Task ValidationCountsSelectedFilesOnceEvenWhenTheFindingIsAPair(bool selectAudioOnly, string expected)
    {
        string folder = TestMediaFixture.NewFolder();
        TestMediaFixture.Seed(folder, Action.AudioGToBin, "keep");
        var model = new MainViewModel(Settings());
        model.SetInputs([selectAudioOnly ? Path.Combine(folder, "song.wav") : folder]);

        await model.ValidateCommand.ExecuteAsync(null);

        Assert.Single(model.LastReport!.Entries);
        Assert.Equal(expected, model.ActivityItemCount);
    }
    [Fact]
    public async Task RestoreOperationOpensPickerWithoutSourceAndDoesNotRunValidation()
    {
        var model = new MainViewModel(Settings());
        model.SelectedOperation = model.Operations.Single(x => x.IsRestore);
        model.FolderPath = "";
        int opened = 0;
        model.RestoreQuarantinedFilesAsync = () => { opened++; return Task.CompletedTask; };
        Assert.True(model.CanStart);
        Assert.False(model.CanChooseSource);
        Assert.False(model.SelectedOperation.IsValidation);
        await model.StartCommand.ExecuteAsync(null);
        Assert.Equal(1, opened);
        Assert.False(model.IsBusy);
        model.SelectedOperation = model.Operations.Single(x => x.IsValidation);
        Assert.False(model.CanStart);
        Assert.True(model.CanChooseSource);
    }

    [Fact]
    public async Task TrackGroupingKeepsItsSelectionAndMovesToTheApprovedDestinationByDefault()
    {
        string folder = TestMediaFixture.NewFolder();
        string source = Path.Combine(folder, "BKD33601 - Artist - Title.zip");
        File.WriteAllText(source, "content");
        string destination = folder;
        var model = new MainViewModel(Settings());
        var operation = model.Operations.Single(x => x.Definition.Operation == Action.GroupByTrackId);
        model.SelectedOperation = operation;
        model.AddInputs([folder]);
        Assert.Same(operation, model.SelectedOperation);
        Assert.True(model.CanChooseFiles);
        Assert.True(model.CanStart);
        model.ConfigureTrackGroupingAsync = async dialog =>
        {
            Assert.True(dialog.Move);
            Assert.False(dialog.HasEdits);
            Assert.Equal("Move items", dialog.ApplyLabel);
            dialog.GroupingMode = 1;
            Assert.True(dialog.HasEdits);
            dialog.RangeSeparatorStyle = 1;
            Assert.Equal(destination, dialog.OutputFolder);
            await dialog.PreviewAsync();
            Assert.False(dialog.HasIssues);
            var group = Assert.Single(dialog.Tree);
            Assert.Equal("Expand all", dialog.TreeExpansionLabel);
            dialog.ToggleTreeExpansionCommand.Execute(null);
            Assert.True(group.IsExpanded);
            Assert.Equal("Collapse all", dialog.TreeExpansionLabel);
            dialog.ToggleTreeExpansionCommand.Execute(null);
            Assert.False(group.IsExpanded);
            Assert.Equal("BKD-33601 to BKD-33650", group.Detail);
            Assert.Equal("BKD33601 - Artist - Title.zip", Assert.Single(group.Children).Label);
            Assert.False(Directory.Exists(Path.Combine(destination, "BKD-33601 to BKD-33650")));
            return dialog.GetSubmission();
        };

        await model.StartCommand.ExecuteAsync(null);

        Assert.False(model.IsBusy);
        Assert.Contains(model.Activity, row => row.Message == "Processing: BKD33601 - Artist - Title.zip");
        Assert.Equal("Succeeded", Assert.Single(model.LastReport!.Entries).Status);
        Assert.False(File.Exists(source));
        Assert.Equal("content", File.ReadAllText(Path.Combine(destination, "BKD-33601 to BKD-33650", Path.GetFileName(source))));
    }

    [Fact]
    public async Task TrackGroupingStartRequiresExistingInputsAndLetsPreviewReportUnrecognizedNames()
    {
        string folder = TestMediaFixture.NewFolder();
        string unsupported = Path.Combine(folder, "Unrecognized.zip");
        string supported = Path.Combine(folder, "ABC-123A.zip");
        File.WriteAllText(unsupported, "other");
        File.WriteAllText(supported, "track");
        var model = new MainViewModel(Settings());
        model.SelectedOperation = model.Operations.Single(operation => operation.IsTrackGrouping);
        int previews = 0;
        model.ConfigureTrackGroupingAsync = async dialog =>
        {
            previews++;
            Assert.Equal(folder, dialog.OutputFolder);
            try
            {
                await dialog.PreviewAsync();
                var issue = Assert.Single(dialog.Issues, issue => issue.Source == unsupported);
                Assert.Equal("Unrecognized.zip", issue.SourceName);
                Assert.Contains("No track ID", issue.Reason);
                Assert.False(dialog.CanApply);
                return null;
            }
            finally { dialog.StopPreview(); }
        };
        model.PickTrackGroupingDestinationAsync = () => throw new InvalidOperationException("A single input folder implies the destination.");

        Assert.False(model.StartCommand.CanExecute(null));
        model.FolderPath = Path.Combine(folder, "Missing.zip");
        Assert.False(model.StartCommand.CanExecute(null));
        Assert.Contains("existing file or folder", model.SourceSummary);
        await model.StartCommand.ExecuteAsync(null);
        Assert.Equal(0, previews);

        model.FolderPath = unsupported;
        Assert.True(model.StartCommand.CanExecute(null));
        await model.StartCommand.ExecuteAsync(null);
        Assert.Equal(1, previews);
        model.FolderPath = supported;
        Assert.True(model.StartCommand.CanExecute(null));
        model.FolderPath = folder;
        Assert.True(model.StartCommand.CanExecute(null));
    }

    [Fact]
    public async Task TrackGroupingAsksForAnAmbiguousDestinationBeforeOpeningPreview()
    {
        string first = TestMediaFixture.NewFolder(), second = TestMediaFixture.NewFolder();
        string destination = TestMediaFixture.NewFolder();
        var model = new MainViewModel(Settings());
        model.SelectedOperation = model.Operations.Single(operation => operation.IsTrackGrouping);
        model.AddInputs([first, second]);
        var steps = new List<string>();
        model.PickTrackGroupingDestinationAsync = () =>
        {
            steps.Add("destination");
            return Task.FromResult<string?>(destination);
        };
        model.ConfigureTrackGroupingAsync = dialog =>
        {
            steps.Add("preview");
            Assert.Equal(destination, dialog.OutputFolder);
            dialog.FolderHandling = 1;
            Assert.Equal(destination, dialog.OutputFolder);
            return Task.FromResult<TrackGroupingSubmission?>(null);
        };

        Assert.True(model.StartCommand.CanExecute(null));
        await model.StartCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "destination", "preview" }, steps);
    }

    [Fact]
    public async Task GroupingEditsReuseTheScanUntilRefresh()
    {
        string source = TestMediaFixture.NewFolder();
        string first = Path.Combine(source, "ABC-123.zip");
        string second = Path.Combine(source, "ABC-456.zip");
        File.WriteAllText(first, "first");
        var dialog = new TrackGroupingViewModel([source]);
        try
        {
            await dialog.PreviewAsync();
            Assert.Single(dialog.GetSubmission()!.Plan.Items);
            var previousTree = dialog.Tree;
            File.WriteAllText(second, "second");
            dialog.MinimumIdDigits = 5;
            Assert.Null(dialog.GetSubmission());
            Assert.Same(previousTree, dialog.Tree);
            await dialog.PreviewAsync();
            var item = Assert.Single(dialog.GetSubmission()!.Plan.Items);
            Assert.Equal(Path.Combine(source, "ABC-001xx", "ABC-123.zip"), item.Destination);

            await dialog.PreviewAsync(refresh: true);
            Assert.Equal(2, dialog.GetSubmission()!.Plan.Items.Count);

            string output = TestMediaFixture.NewFolder();
            dialog.OutputFolder = output;
            await dialog.PreviewAsync();
            Assert.NotEmpty(dialog.Tree);
            Assert.Null(dialog.GetSubmission());
            await dialog.PreviewAsync(refresh: true);
            Assert.Equal(2, dialog.GetSubmission()!.Plan.Items.Count);
        }
        finally { dialog.StopPreview(); }
    }

    [Fact]
    public async Task SuccessfulRunDisablesStartUntilBackAndCannotRunTwice()
    {
        string folder = TestMediaFixture.NewFolder();
        TestMediaFixture.Seed(folder, Action.ZipToBin, "keep");
        var model = new MainViewModel(Settings());
        model.SetInputs([Path.Combine(folder, "song.zip")]);
        model.SaveOptions(model.Options with
        {
            DeleteOldFiles = false,
            AutoCreateCueText = false
        });
        await model.StartCommand.ExecuteAsync(null);
        var report = model.LastReport;
        Assert.Equal("Succeeded", Assert.Single(report!.Entries).Status);
        Assert.False(model.StartCommand.CanExecute(null));
        await model.StartCommand.ExecuteAsync(null);
        Assert.Same(report, model.LastReport);
        model.ShowProgress = false;
        Assert.True(model.StartCommand.CanExecute(null));
    }

    [Fact]
    public async Task ValidationIsAReadOnlyOperationStartedWithStart()
    {
        string folder = TestMediaFixture.NewFolder();
        File.WriteAllText(Path.Combine(folder, "bad.bin"), "bad");
        var model = new MainViewModel(Settings()) { FolderPath = folder };
        model.SelectedOperation = model.Operations.Single(x => x.IsValidation);
        await model.StartCommand.ExecuteAsync(null);
        Assert.Equal("Validate Karaoke Files", model.LastReport!.Operation);
        Assert.Equal("Damaged", Assert.Single(model.LastReport.Entries).Status);
        Assert.Equal("bad", File.ReadAllText(Path.Combine(folder, "bad.bin")));
        Assert.False(model.StartCommand.CanExecute(null));
    }

}
