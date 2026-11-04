using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Recovery;
using KaraokeToolkit.Core.Settings;
using KaraokeToolkit.Core;
using Action = KaraokeToolkit.Core.Conversion.OperationCatalog.defAction;
namespace KaraokeToolkit.Tests.Conversion;

public class OutputPolicyTests
{

    [Fact] public async Task DisabledGenerationCreatesNoMetadataAndDoesNotPromptForIt()
    {
        string folder=TestMediaFixture.NewFolder();TestMediaFixture.Seed(folder,Action.AudioG_To_Bin,"keep");int calls=0;
        string cue=Path.Combine(folder,new DirectoryInfo(folder).Name+".cue");File.WriteAllText(cue,"keep");
        var result=await new ConversionService().RunAsync(new(Action.AudioG_To_Bin,folder,new(){AutoCreateCueText=false,DeleteOldFiles=false}),OverwritePolicy.Ask,
            resolveConflict:(_,_)=>{calls++;return Task.FromResult(ConflictDecision.Replace);});
        Assert.Equal(1,result.Succeeded);Assert.Equal(0,calls);Assert.Equal("keep",File.ReadAllText(cue));Assert.Empty(Directory.GetFiles(folder,"*.txt"));
    }
    [Theory]
    [InlineData(ConflictDecision.Skip)]
    [InlineData(ConflictDecision.Replace)]
    [InlineData(ConflictDecision.Cancel)]
    public async Task ExistingCuePromptsAndHonorsDecision(ConflictDecision decision)
    {
        string folder=TestMediaFixture.NewFolder();TestMediaFixture.Seed(folder,Action.Bin_To_AudioG,"keep");
        string cue=Path.Combine(folder,new DirectoryInfo(folder).Name+".cue");File.WriteAllText(cue,"keep");int calls=0;
        var result=await new ConversionService().RunAsync(new(Action.Create_Cue_And_Text_Files,folder,new()),OverwritePolicy.Ask,
            resolveConflict:(conflict,_)=>{calls++;Assert.Contains(cue,conflict.ExistingFiles);return Task.FromResult(decision);});
        Assert.Equal(1,calls);
        if(decision==ConflictDecision.Replace){Assert.Equal(1,result.Succeeded);Assert.Contains("TRACK 01",File.ReadAllText(cue));}
        else {Assert.Equal("keep",File.ReadAllText(cue));Assert.Empty(Directory.GetFiles(folder,"*.txt"));Assert.Equal(decision==ConflictDecision.Cancel,result.Cancelled);}
        Assert.True(File.Exists(Path.Combine(folder,"song.bin")));
    }
    [Fact] public async Task AskWithoutAResolverNeverOverwrites()
    {
        string folder=TestMediaFixture.NewFolder();TestMediaFixture.Seed(folder,Action.AudioG_To_Bin,"overwrite");string bin=Path.Combine(folder,"song.bin"),hash=FileTransaction.Hash(bin);
        var result=await new ConversionService().RunAsync(new(Action.AudioG_To_Bin,folder,new(){AutoCreateCueText=false,DeleteOldFiles=true}),OverwritePolicy.Ask);
        Assert.Equal(1,result.Failed);Assert.Equal(hash,FileTransaction.Hash(bin));Assert.True(File.Exists(Path.Combine(folder,"song.wav")));
    }
    [Fact] public async Task OutputChangedDuringPromptIsNotOverwritten()
    {
        string folder=TestMediaFixture.NewFolder();TestMediaFixture.Seed(folder,Action.AudioG_To_Bin,"overwrite");string bin=Path.Combine(folder,"song.bin");
        var result=await new ConversionService().RunAsync(new(Action.AudioG_To_Bin,folder,new(){AutoCreateCueText=false,DeleteOldFiles=true}),OverwritePolicy.Ask,
            resolveConflict:(_,_)=>{File.WriteAllText(bin,"updated by someone else");return Task.FromResult(ConflictDecision.Replace);});
        Assert.Equal(1,result.Failed);Assert.Equal("updated by someone else",File.ReadAllText(bin));Assert.True(File.Exists(Path.Combine(folder,"song.wav")));
    }}
