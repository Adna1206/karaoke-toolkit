using KaraokeToolkit.App.Features.Settings;
using KaraokeToolkit.App.Features.Main;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core;
using Action=KaraokeToolkit.Core.Conversion.OperationCatalog.defAction;
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
    [InlineData(Action.AudioG_To_Bin,Action.AudioG_To_Bin,"song.bin")]
    [InlineData(Action.AudioG_To_Zip,Action.AudioG_To_Bin,"song.zip")]
    [InlineData(Action.Zip_To_AudioG,Action.Zip_To_Bin,"song.wav")]
    [InlineData(Action.Zip_To_Bin,Action.Zip_To_Bin,"song.bin")]
    [InlineData(Action.Bin_To_AudioG,Action.Bin_To_AudioG,"song.mp3")]
    [InlineData(Action.Bin_To_Zip,Action.Bin_To_AudioG,"song.zip")]
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
    [Fact] public async Task MissingFolderFailureReturnsToEditableStateAndNextRunSucceeds()
    {
        var model=Model(Path.Combine(TestMediaFixture.NewFolder(),"absent"),Action.AudioG_To_Bin);
        await model.StartCommand.ExecuteAsync(null);Assert.False(model.IsBusy);Assert.Contains("existing source folder",model.Status);
        model.ShowProgress=false;model.FolderPath=TestMediaFixture.NewFolder();TestMediaFixture.Seed(model.FolderPath,Action.AudioG_To_Bin,"keep");
        await model.StartCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(30));Assert.True(File.Exists(Path.Combine(model.FolderPath,"song.bin")));
    }
    [Fact] public async Task CancelCommandAtStartDoesNotDeleteInputsOrLeaveBusyState()
    {
        string folder=TestMediaFixture.NewFolder();TestMediaFixture.Seed(folder,Action.AudioG_To_Bin,"keep");var before=TestMediaFixture.Snapshot(folder);var model=Model(folder,Action.AudioG_To_Bin);
        model.PropertyChanged+=(_,e)=>{if(e.PropertyName==nameof(model.ShowProgress)&&model.ShowProgress)model.CancelCommand.Execute(null);};
        await model.StartCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(model.CancelRequested);Assert.False(model.IsBusy);Assert.False(model.CancelCommand.CanExecute(null));Assert.Equal(before,TestMediaFixture.Snapshot(folder));
    }
}
