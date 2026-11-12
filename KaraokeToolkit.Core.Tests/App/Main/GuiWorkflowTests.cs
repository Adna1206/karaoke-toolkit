using KaraokeToolkit.Core.Audio;
using KaraokeToolkit.Core.Archives;
using KaraokeToolkit.App.Features.Settings;
using KaraokeToolkit.App.Features.Main;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Settings;
using KaraokeToolkit.Core;
using Action=KaraokeToolkit.Core.Conversion.OperationCatalog.defAction;
namespace KaraokeToolkit.Core.Tests.App.Main;
public class GuiWorkflowTests
{
    static string SettingsPath()=>Path.Combine(TestMediaFixture.NewFolder(),"options.xmls");
    [Fact] public void OptionsDraftDoesNotChangeAppliedOptionsUntilSaved()
    {
        var model=new MainViewModel(SettingsPath());var draft=new OptionsViewModel(model.Options);
        draft.DeleteOriginals=false;draft.CompressionLevel=5;
        Assert.True(model.Options.DeleteOldFiles);Assert.Equal(ZipCompression.Balanced,model.Options.ZipCompression);
        model.SaveOptions(draft.ToOptions());Assert.False(model.Options.DeleteOldFiles);Assert.Equal(ZipCompression.Balanced,model.Options.ZipCompression);
    }
    [Fact] public void SavedOptionsReloadIncludingBinRenaming()
    {
        string path=SettingsPath();var model=new MainViewModel(path);
        var expected=model.Options with {DeleteOldFiles=false,RenameBins=false,ZipCompression=ZipCompression.SmallestSize,
            RecurseFolders=false,AutoCreateCueText=false,AutoRenameZip=false,AudioEncoder=AudioEncoder.OGG,
            AudioBitrate=192,NamingFormat="ARTIST - TITLE",LastDiscNumber="TEST01",LastOutputFolder="output"};
        model.SaveOptions(expected);
        var reopened=new MainViewModel(path);Assert.Equal(expected,reopened.Options);
        var discardedDraft=new OptionsViewModel(reopened.Options){RenameBins=true,Bitrate=320};
        Assert.Equal(expected,new MainViewModel(path).Options);
    }
    [Fact] public void LegacySettingsLoadAndUpgradeWithoutLosingOptions()
    {
        string path=SettingsPath();
        var original=new ConversionOptions {DeleteOldFiles=false,ZipCompression=ZipCompression.Balanced};
        new SettingsStore(path).Save(original);
        var model=new MainViewModel(path);Assert.Equal(original,model.Options);
        model.SaveOptions(model.Options with {RenameBins=false});
        Assert.False(new MainViewModel(path).Options.RenameBins);
        Assert.Equal(original,new SettingsStore(path).Load());
    }
    [Fact] public void OriginalFolderActionOrderIsPreservedAndUnavailableActionsCannotStart()
    {
        var model=new MainViewModel(SettingsPath()){FolderPath="."};
        Assert.Equal(new[]{Action.Zip_To_Bin,Action.AudioG_To_Bin,Action.Bin_To_Zip,Action.AudioG_To_Zip,Action.Zip_To_AudioG,Action.Bin_To_AudioG,Action.Repack_Zip,Action.Archive_To_Zip,Action.LosslessG_To_Mp3G,Action.Karaoke_To_Video,Action.Rename_Files_From_Internet,Action.Create_Cue_And_Text_Files,Action.Cleanup_Filenames},model.Operations.Where(x=>x.Action.HasValue).Select(x=>x.Action!.Value));
        Assert.True(model.Operations.Last().IsValidation);
        foreach(var choice in model.Operations){model.SelectedOperation=choice;Assert.Equal(choice.Available && !choice.IsRip,model.StartCommand.CanExecute(null));}
        model.IsBusy=true;Assert.False(model.StartCommand.CanExecute(null));
    }
    [Fact]
    public async Task WmaEncodingCanRunFromTheGui()
    {
        string folder=TestMediaFixture.NewFolder();TestMediaFixture.Seed(folder,Action.Bin_To_AudioG,"keep");
        var model=new MainViewModel(SettingsPath()){FolderPath=folder};
        model.SelectedOperation=model.Operations.Single(x=>x.Action==Action.Bin_To_AudioG);
        model.SaveOptions(model.Options with {AudioEncoder=AudioEncoder.WMA,DeleteOldFiles=false,AutoCreateCueText=false});
        await model.StartCommand.ExecuteAsync(null);
        Assert.Equal("Succeeded",Assert.Single(model.LastReport!.Entries).Status);
        Assert.True(File.Exists(Path.Combine(folder,"song.wma")));
    }
    [Fact] public void CorruptSettingsShowErrorWithoutOverwritingFile()
    {
        string path=SettingsPath();File.WriteAllText(path,"<broken");var model=new MainViewModel(path);
        Assert.Contains("Could not load options",model.Status);Assert.Equal("<broken",File.ReadAllText(path));
    }
}




