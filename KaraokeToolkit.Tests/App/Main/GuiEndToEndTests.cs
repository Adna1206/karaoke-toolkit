using KaraokeToolkit.Core.Execution;
using KaraokeToolkit.App.Features.Settings;
using KaraokeToolkit.App.Features.Main;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Settings;
using KaraokeToolkit.Core;
using Action=KaraokeToolkit.Core.Execution.OperationId;
namespace KaraokeToolkit.Tests.App.Main;
public class GuiEndToEndTests
{
    static MainViewModel Model(string folder,Action action)
    {
        var model=new MainViewModel(Path.Combine(TestMediaFixture.NewFolder(),"options.xmls")){FolderPath=folder};
        model.SelectedOperation=model.Operations.Single(x=>x.Action==action);
        model.SaveOptions(model.Options with {DeleteOldFiles=false,RenameBins=false,AutoRenameZip=false,AutoCreateCueText=false});
        return model;
    }
    [Theory]
    [InlineData(Action.AudioGToBin,Action.AudioGToBin,"song.bin")]
    [InlineData(Action.AudioGToZip,Action.AudioGToBin,"song.zip")]
    [InlineData(Action.ZipToAudioG,Action.ZipToBin,"song.wav")]
    [InlineData(Action.ZipToBin,Action.ZipToBin,"song.bin")]
    [InlineData(Action.BinToAudioG,Action.BinToAudioG,"song.mp3")]
    [InlineData(Action.BinToZip,Action.BinToAudioG,"song.zip")]
    public async Task StartCommandRunsRealEngineAndCanBeReused(Action action,Action seed,string output)
    {
        string folder=TestMediaFixture.NewFolder();TestMediaFixture.Seed(folder,seed,"keep");
        var model=Model(folder,action);bool observedBusy=false;
        model.PropertyChanged+=(_,e)=>{if(e.PropertyName==nameof(model.IsBusy)&&model.IsBusy){observedBusy=true;Assert.False(model.StartCommand.CanExecute(null));Assert.False(model.CanEdit);}};
        await model.StartCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(observedBusy);Assert.True(model.ShowProgress);Assert.False(model.IsBusy);Assert.True(model.CanEdit);
        Assert.True(new FileInfo(Path.Combine(folder,output)).Length>0);
        Assert.Contains(model.Activity,x=>x.Message.StartsWith("Processing finished"));
        string second=TestMediaFixture.NewFolder();TestMediaFixture.Seed(second,seed,"keep");model.ShowProgress=false;model.FolderPath=second;
        await model.StartCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(new FileInfo(Path.Combine(second,output)).Length>0);Assert.False(model.IsBusy);Assert.False(model.StartCommand.CanExecute(null));
        model.ShowProgress=false; Assert.True(model.StartCommand.CanExecute(null));
    }
    [Fact] public async Task MissingFolderBlocksStartUntilAnExistingFolderIsSelected()
    {
        var model=Model(Path.Combine(TestMediaFixture.NewFolder(),"absent"),Action.AudioGToBin);
        Assert.False(model.StartCommand.CanExecute(null));
        Assert.True(model.CanEdit);
        Assert.Equal("Choose an existing file or folder.", model.SourceSummary);

        model.FolderPath = TestMediaFixture.NewFolder();
        TestMediaFixture.Seed(model.FolderPath, Action.AudioGToBin, "keep");
        Assert.True(model.StartCommand.CanExecute(null));
        await model.StartCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(File.Exists(Path.Combine(model.FolderPath, "song.bin")));
    }
    [Fact]
    public async Task ActivityShowsRelativeSkipReasonAndReportRetainsFullPath()
    {
        string folder = TestMediaFixture.NewFolder();
        string child = Path.Combine(folder, "ABBA");
        TestMediaFixture.Seed(child, Action.AudioGToBin, "overwrite");
        var model = Model(folder, Action.AudioGToBin);
        model.SaveOptions(model.Options with { RecurseFolders = true }, OverwritePolicy.Skip);

        await model.StartCommand.ExecuteAsync(null);

        Assert.Single(model.Activity, entry => entry.Message == "Skipped: ABBA/song.cdg — Existing output retained.");
        Assert.DoesNotContain(model.Activity, entry => entry.Message.StartsWith("Succeeded:"));
        var reportEntry = Assert.Single(model.LastReport!.Entries);
        Assert.Equal(Path.Combine(child, "song.cdg"), reportEntry.Source);
        Assert.Equal("Existing output retained.", reportEntry.Message);
    }

    [Fact]
    public async Task FailedItemsCanBeRetriedWithoutLosingTheSavedRetryScope()
    {
        string folder = TestMediaFixture.NewFolder();
        TestMediaFixture.Seed(folder, Action.AudioGToBin, "overwrite");
        var model = Model(folder, Action.AudioGToBin);
        model.SaveOptions(model.Options, OverwritePolicy.Fail);

        await model.StartCommand.ExecuteAsync(null);

        Assert.False(model.IsBusy);
        Assert.True(model.CanRetry);
        Assert.Equal("Failed", Assert.Single(model.LastReport!.Entries).Status);

        File.Delete(Path.Combine(folder, "song.bin"));
        await model.RetryFailedAsync();

        Assert.False(model.IsBusy);
        Assert.False(model.CanRetry);
        Assert.Equal("Succeeded", Assert.Single(model.LastReport!.Entries).Status);
        Assert.True(File.Exists(Path.Combine(folder, "song.bin")));
    }

    [Fact]
    public async Task CancelCommandAtStartDoesNotDeleteInputsOrLeaveBusyState()
    {
        string folder = TestMediaFixture.NewFolder();
        TestMediaFixture.Seed(folder, Action.AudioGToBin, "keep");
        var before = TestMediaFixture.Snapshot(folder);
        var model = Model(folder, Action.AudioGToBin);
        void CancelAtStart(object? sender, System.ComponentModel.PropertyChangedEventArgs change)
        {
            if (change.PropertyName == nameof(model.ShowProgress) && model.ShowProgress)
                model.CancelCommand.Execute(null);
        }
        model.PropertyChanged += CancelAtStart;

        await model.StartCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(30));

        Assert.True(model.CancelRequested);
        Assert.False(model.IsBusy);
        Assert.False(model.CancelCommand.CanExecute(null));
        Assert.Equal(before, TestMediaFixture.Snapshot(folder));

        model.PropertyChanged -= CancelAtStart;
        model.ShowProgress = false;
        await model.StartCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(30));

        Assert.False(model.CancelRequested);
        Assert.False(model.IsBusy);
        Assert.Equal(RunStatus.Success, model.StatusOutcome);
        Assert.True(File.Exists(Path.Combine(folder, "song.bin")));
    }
}
