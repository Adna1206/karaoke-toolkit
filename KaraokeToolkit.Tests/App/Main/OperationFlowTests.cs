using KaraokeToolkit.Core.Execution;
using KaraokeToolkit.App.Features.Main;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Recovery;
using KaraokeToolkit.Core.Settings;
using KaraokeToolkit.Core;
using Action = KaraokeToolkit.Core.Execution.OperationId;

namespace KaraokeToolkit.Tests.App.Main;

public class OperationFlowTests
{
    private static string Settings() => Path.Combine(TestMediaFixture.NewFolder(), "options.xmls");
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
            Assert.Equal("Move items", dialog.ApplyLabel);
            dialog.GroupingMode = 1;
            dialog.RangeSeparatorStyle = 1;
            Assert.Equal(destination, dialog.OutputFolder);
            await dialog.PreviewAsync();
            Assert.False(dialog.HasIssues);
            Assert.Contains("BKD33601 - Artist - Title.zip → BKD-33601 to BKD-33650", dialog.Example);
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
