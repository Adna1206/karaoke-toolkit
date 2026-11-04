using KaraokeToolkit.Core.DiscRipping;
using KaraokeToolkit.Core.Audio;
using KaraokeToolkit.App.Features.Settings;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Recovery;
using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using KaraokeToolkit.Core;
using Action = KaraokeToolkit.Core.Conversion.OperationCatalog.defAction;
namespace KaraokeToolkit.Tests.Conversion;

public class ModernConversionTests
{
    static ConversionOptions Options(bool delete=false) => new() {DeleteOldFiles=delete,RenameBins=false,AutoRenameZip=false,AutoCreateCueText=false};
    static async Task<ConversionResult> Run(string folder, Action action, OverwritePolicy policy=OverwritePolicy.Fail, bool delete=false,
        CancellationToken token=default) => await new ConversionService().RunAsync(new(action,folder,Options(delete)),policy,cancellationToken:token);

    [Theory]
    [InlineData(Action.AudioG_To_Bin,Action.AudioG_To_Bin,"song.bin")]
    [InlineData(Action.AudioG_To_Zip,Action.AudioG_To_Bin,"song.zip")]
    [InlineData(Action.Zip_To_Bin,Action.Zip_To_Bin,"song.bin")]
    [InlineData(Action.Zip_To_AudioG,Action.Zip_To_Bin,"song.wav")]
    [InlineData(Action.Bin_To_AudioG,Action.Bin_To_AudioG,"song.mp3")]
    [InlineData(Action.Bin_To_Zip,Action.Bin_To_AudioG,"song.zip")]
    public async Task ConversionsPublishVerifiedOutputsAndDeleteOnlyAfterSuccess(Action action,Action seed,string output)
    {
        string folder=TestMediaFixture.NewFolder();TestMediaFixture.Seed(folder,seed,"keep");
        string[] originals=Directory.GetFiles(folder);
        var result=await Run(folder,action,delete:true);
        Assert.Equal(0,result.Failed);Assert.Equal(1,result.Succeeded);
        Assert.True(new FileInfo(Path.Combine(folder,output)).Length>0);
        Assert.All(originals,p=>Assert.False(File.Exists(p)));
        Assert.Empty(Directory.GetDirectories(folder,".karaoke-job-*"));
    }
    [Theory]
    [InlineData(OverwritePolicy.Fail,ConversionStatus.Failed)]
    [InlineData(OverwritePolicy.Skip,ConversionStatus.Skipped)]
    [InlineData(OverwritePolicy.Replace,ConversionStatus.Succeeded)]
    public async Task ExistingBinHasExplicitOverwritePolicy(OverwritePolicy policy,ConversionStatus expected)
    {
        string folder=TestMediaFixture.NewFolder();TestMediaFixture.Seed(folder,Action.AudioG_To_Bin,"overwrite");
        string bin=Path.Combine(folder,"song.bin");string before=FileTransaction.Hash(bin);
        var result=await Run(folder,Action.AudioG_To_Bin,policy,delete:true);
        Assert.Equal(expected,Assert.Single(result.Files).Status);
        if(policy==OverwritePolicy.Replace){Assert.NotEqual(before,FileTransaction.Hash(bin));Assert.Equal(150*2448,new FileInfo(bin).Length);}
        else {Assert.Equal(before,FileTransaction.Hash(bin));Assert.True(File.Exists(Path.Combine(folder,"song.wav")));}
    }
    [Theory]
    [InlineData("invalid-audio")]
    [InlineData("missing-audio")]
    public async Task InvalidInputNeverDeletesOrPublishes(string mode)
    {
        string folder=TestMediaFixture.NewFolder();TestMediaFixture.Seed(folder,Action.AudioG_To_Bin,mode);var before=TestMediaFixture.Snapshot(folder);
        var result=await Run(folder,Action.AudioG_To_Bin,delete:true);
        Assert.Equal(1,result.Failed);Assert.Equal(before,TestMediaFixture.Snapshot(folder));
    }
    [Fact] public async Task TruncatedBinFailsWithoutDeletingInput()
    {
        string folder=TestMediaFixture.NewFolder();TestMediaFixture.Seed(folder,Action.Bin_To_AudioG,"truncated-bin");var before=TestMediaFixture.Snapshot(folder);
        Assert.Equal(1,(await Run(folder,Action.Bin_To_AudioG,delete:true)).Failed);Assert.Equal(before,TestMediaFixture.Snapshot(folder));
    }
    [Fact] public async Task PaddingRetainsAllAudioAndGraphics()
    {
        string folder=TestMediaFixture.NewFolder();TestMediaFixture.Seed(folder,Action.AudioG_To_Bin,"short-graphics");
        string graphics=Path.Combine(folder,"song.cdg");File.WriteAllBytes(graphics,new byte[120]);
        var result=await Run(folder,Action.AudioG_To_Bin);Assert.Equal(1,result.Succeeded);
        string bin=Path.Combine(folder,"song.bin");Assert.Equal(150*2448,new FileInfo(bin).Length);
        await BinConverter.SplitAsync(bin,Path.Combine(folder,"decoded.wav"),Path.Combine(folder,"decoded.cdg"),default);
        Assert.Equal(File.ReadAllBytes(Path.Combine(folder,"song.wav")),File.ReadAllBytes(Path.Combine(folder,"decoded.wav")));
    }
    [Fact] public async Task CorruptZipCrcFailsWithoutDeletingArchive()
    {
        string folder=TestMediaFixture.NewFolder();string path=Path.Combine(folder,"bad.zip");
        using(var zip=ZipFile.Open(path,ZipArchiveMode.Create)){
            using(var e=zip.CreateEntry("song.wav",CompressionLevel.NoCompression).Open())e.Write([1,2,3,4]);
            using(var e=zip.CreateEntry("song.cdg",CompressionLevel.NoCompression).Open())e.Write(new byte[96]);
        }
        byte[] data=File.ReadAllBytes(path);int index=Find(data,[1,2,3,4]);Assert.True(index>=0);data[index]=5;File.WriteAllBytes(path,data);
        var result=await Run(folder,Action.Zip_To_AudioG,delete:true);Assert.Equal(1,result.Failed);Assert.Contains("CRC",result.Files[0].Message);Assert.True(File.Exists(path));
    }
    static int Find(byte[] bytes,byte[] pattern){for(int i=0;i<=bytes.Length-pattern.Length;i++)if(bytes.AsSpan(i,pattern.Length).SequenceEqual(pattern))return i;return -1;}
    [Fact] public async Task UnsafeAndAmbiguousArchivesDoNotExtract()
    {
        foreach(string name in new[]{"../song.cdg","second.cdg"}){
            string folder=TestMediaFixture.NewFolder();TestMediaFixture.Seed(folder,Action.Zip_To_Bin,"keep");
            using(var zip=ZipFile.Open(Path.Combine(folder,"song.zip"),ZipArchiveMode.Update))using(var e=zip.CreateEntry(name).Open())e.Write(new byte[96]);
            Assert.Equal(1,(await Run(folder,Action.Zip_To_AudioG,delete:true)).Failed);Assert.Single(Directory.GetFiles(folder));
        }
    }
    [Fact] public async Task FailureOfOneInputDoesNotHideSuccessOfAnother()
    {
        string folder=TestMediaFixture.NewFolder();TestMediaFixture.Seed(folder,Action.AudioG_To_Bin,"keep");File.WriteAllBytes(Path.Combine(folder,"broken.cdg"),new byte[96]);
        var result=await Run(folder,Action.AudioG_To_Bin);Assert.Equal(1,result.Failed);Assert.Equal(1,result.Succeeded);
    }
    [Fact] public async Task CancelBetweenItemsRetainsUnprocessedInputs()
    {
        string folder=TestMediaFixture.NewFolder();TestMediaFixture.Seed(folder,Action.AudioG_To_Bin,"keep");string child=Path.Combine(folder,"child");TestMediaFixture.Seed(child,Action.AudioG_To_Bin,"keep");
        using var cancel=new CancellationTokenSource();
        var progress=new Immediate<ConversionProgress>(p=>{if(p.Completed==1)cancel.Cancel();});
        var result=await new ConversionService().RunAsync(new(Action.AudioG_To_Bin,folder,Options()),progress:progress,cancellationToken:cancel.Token);
        Assert.True(result.Cancelled);Assert.True(result.Succeeded == 1, string.Join("; ", result.Files.Select(f => f.Message)));Assert.Single(Directory.GetFiles(folder,"*.bin",SearchOption.AllDirectories));
        Assert.Equal(2,Directory.GetFiles(folder,"*.wav",SearchOption.AllDirectories).Length);
    }
    [Fact] public async Task CancellationDuringStagingLeavesSourcesUntouched()
    {
        string folder=TestMediaFixture.NewFolder();TestMediaFixture.Seed(folder,Action.AudioG_To_Bin,"keep");var before=TestMediaFixture.Snapshot(folder);
        using var cancel=new CancellationTokenSource();var progress=new Immediate<ConversionProgress>(p=>{if(p.Message.StartsWith("Processing:"))cancel.Cancel();});
        var result=await new ConversionService().RunAsync(new(Action.AudioG_To_Bin,folder,Options(true)),progress:progress,cancellationToken:cancel.Token);
        Assert.True(result.Cancelled);Assert.Equal(before,TestMediaFixture.Snapshot(folder));
    }
    [Fact] public async Task ReadOnlyTargetPreservesBothOutputsAndSources()
    {
        string folder=TestMediaFixture.NewFolder();TestMediaFixture.Seed(folder,Action.Bin_To_AudioG,"keep");string target=Path.Combine(folder,"song.cdg");File.WriteAllText(target,"keep");File.SetAttributes(target,FileAttributes.ReadOnly);
        try {var result=await Run(folder,Action.Bin_To_AudioG,OverwritePolicy.Replace,true);Assert.Equal(1,result.Failed);Assert.Equal("keep",File.ReadAllText(target));Assert.False(File.Exists(Path.Combine(folder,"song.mp3")));Assert.True(File.Exists(Path.Combine(folder,"song.bin")));}
        finally {File.SetAttributes(target,FileAttributes.Normal);}
    }
    [Theory]
    [InlineData(AudioEncoder.MP3,128)]
    [InlineData(AudioEncoder.MP3,192)]
    [InlineData(AudioEncoder.OGG,128)]
    [InlineData(AudioEncoder.OGG,192)]
    public async Task EncodedAudioHasRequestedBitrate(AudioEncoder encoder,int rate)
    {
        string folder=TestMediaFixture.NewFolder();TestMediaFixture.Seed(folder,Action.Bin_To_AudioG,"keep");
        var result=await new ConversionService().RunAsync(new(Action.Bin_To_AudioG,folder,Options() with{AudioEncoder=encoder,AudioBitrate=rate}));
        Assert.True(result.Failed==0,string.Join(";",result.Files.Select(x=>x.Message)));
        byte[] bytes=File.ReadAllBytes(Path.Combine(folder,encoder==AudioEncoder.MP3?"song.mp3":"song.ogg"));
        if(encoder==AudioEncoder.OGG){int i=Find(bytes,Encoding.ASCII.GetBytes("\x01vorbis"));Assert.True(i>=0);Assert.Equal(rate*1000,BitConverter.ToInt32(bytes,i+20));}
        else{
            int[] rates=[0,32,40,48,56,64,80,96,112,128,160,192,224,256,320];bool found=false;
            for(int i=0;i<bytes.Length-4;i++)if(bytes[i]==255&&(bytes[i+1]&0xfe)==0xfa){int n=bytes[i+2]>>4;if(n>0&&n<15){Assert.Equal(rate,rates[n]);found=true;break;}}
            Assert.True(found,"No MPEG1 Layer III frame found.");
        }
    }
    [Theory]
    [InlineData(AudioEncoder.WMA, "wma")]
    [InlineData(AudioEncoder.WAV, "wav")]
    [InlineData(AudioEncoder.FLAC, "flac")]
    [InlineData(AudioEncoder.AAC, "m4a")]
    public async Task BinConversionPublishesChosenAudioFormat(AudioEncoder encoder, string extension)
    {
        string folder=TestMediaFixture.NewFolder();TestMediaFixture.Seed(folder,Action.Bin_To_AudioG,"keep");
        var options=Options() with {AudioEncoder=encoder,DeleteOldFiles=false,AutoCreateCueText=false};
        var result=await new ConversionService(new RecoveryStore(TestMediaFixture.NewFolder())).RunAsync(new(Action.Bin_To_AudioG,folder,options));
        Assert.True(result.Failed==0,string.Join(";",result.Files.Select(x=>x.Message)));
        Assert.True(File.Exists(Path.Combine(folder,"song."+extension)));
        Assert.True(File.Exists(Path.Combine(folder,"song.cdg")));
    }
    [Theory]
    [InlineData(AudioEncoder.WMA, "wma")]
    [InlineData(AudioEncoder.WAV, "wav")]
    [InlineData(AudioEncoder.FLAC, "flac")]
    [InlineData(AudioEncoder.AAC, "m4a")]
    public async Task BinZipContainsChosenAudioFormat(AudioEncoder encoder, string extension)
    {
        string folder=TestMediaFixture.NewFolder();TestMediaFixture.Seed(folder,Action.Bin_To_Zip,"keep");
        var options=Options() with {AudioEncoder=encoder,DeleteOldFiles=false,AutoCreateCueText=false};
        var result=await new ConversionService(new RecoveryStore(TestMediaFixture.NewFolder())).RunAsync(new(Action.Bin_To_Zip,folder,options));
        Assert.True(result.Failed==0,string.Join(";",result.Files.Select(x=>x.Message)));
        using var zip=ZipFile.OpenRead(Path.Combine(folder,"song.zip"));
        Assert.NotNull(zip.GetEntry("song."+extension));
        Assert.NotNull(zip.GetEntry("song.cdg"));
    }
    [Fact] public async Task CleanupRenamesPairAndRefusesCollisions()
    {
        string folder=TestMediaFixture.NewFolder();File.WriteAllText(Path.Combine(folder,"disc01_artist_song.mp3"),"audio");File.WriteAllText(Path.Combine(folder,"disc01_artist_song.cdg"),"graphics");
        var result=await Run(folder,Action.Cleanup_Filenames);Assert.Equal(1,result.Succeeded);
        Assert.Equal("audio",File.ReadAllText(Path.Combine(folder,"DISC01 Artist Song.mp3")));Assert.True(File.Exists(Path.Combine(folder,"DISC01 Artist Song.cdg")));
        File.WriteAllText(Path.Combine(folder,"disc01_artist_song.mp3"),"other");var before=TestMediaFixture.Snapshot(folder);
        Assert.Equal(1,(await Run(folder,Action.Cleanup_Filenames)).Failed);Assert.Equal(before,TestMediaFixture.Snapshot(folder));
    }
    [Fact] public async Task CleanupSupportsCaseOnlyRename()
    {
        string folder=TestMediaFixture.NewFolder();File.WriteAllText(Path.Combine(folder,"disc01 artist.mp3"),"audio");
        Assert.Equal(1,(await Run(folder,Action.Cleanup_Filenames)).Succeeded);Assert.Equal("DISC01 Artist.mp3",Path.GetFileName(Assert.Single(Directory.GetFiles(folder))));
    }
    [Fact] public async Task CueAndTextHaveConsistentHeadersAndUseRelativePaths()
    {
        string folder=TestMediaFixture.NewFolder();TestMediaFixture.Seed(folder,Action.Bin_To_AudioG,"keep");string name=new DirectoryInfo(folder).Name;
        var result=await Run(folder,Action.Create_Cue_And_Text_Files);Assert.Equal(1,result.Succeeded);
        string cue = File.ReadAllText(Path.Combine(folder, name + ".cue"));
        string text = File.ReadAllText(Path.Combine(folder, name + ".txt"));
        Assert.StartsWith("REM App Name: " + ApplicationInfo.Name + "\r\nREM App Version: ", cue);
        Assert.StartsWith("App Name: " + ApplicationInfo.Name + "\r\nApp Version: ", text);
        Assert.DoesNotContain("REM ", text);
        Assert.Contains("REM Report Type: CD+G CUE Sheet\r\n\r\n", cue);
        Assert.Contains("Report Type: Track Listing\r\n\r\n", text);
        Assert.Equal("FILE \"song.bin\" BINARY\r\n  TRACK 01 CDG\r\n    INDEX 01 00:00:00\r\n", cue.Split("\r\n\r\n")[1]);
        Assert.Equal("01. song\r\n", text.Split("\r\n\r\n")[1]);
        var cueHeader = cue.Split("\r\n").Take(4).Select(line => line[4..]);
        Assert.Equal(cueHeader, text.Split("\r\n").Take(4));
        Assert.Equal(1,(await Run(folder,Action.Create_Cue_And_Text_Files,OverwritePolicy.Skip)).Skipped);
    }
    [Fact] public async Task CancelAfterVerificationPublishesNothingAndKeepsOriginals()
    {
        string folder=TestMediaFixture.NewFolder();TestMediaFixture.Seed(folder,Action.AudioG_To_Bin,"overwrite");var before=TestMediaFixture.Snapshot(folder);
        using var cancel=new CancellationTokenSource();
        var progress=new Immediate<ConversionProgress>(p=>{if(p.FilePercent==90)cancel.Cancel();});
        var result=await new ConversionService().RunAsync(new(Action.AudioG_To_Bin,folder,Options(true)),OverwritePolicy.Replace,progress,cancel.Token);
        Assert.True(result.Cancelled);Assert.Equal(before,TestMediaFixture.Snapshot(folder));
    }
    [Fact] public async Task LocalBinConversionDoesNotInvokeLegacyOnlinePreferences()
    {
        string folder=TestMediaFixture.NewFolder();TestMediaFixture.Seed(folder,Action.Bin_To_AudioG,"keep");
        var result=await new ConversionService().RunAsync(new(Action.Bin_To_AudioG,folder,Options() with{RenameBins=true,AutoRenameZip=true}));
        Assert.Equal(1,result.Succeeded);
    }
    [Fact] public async Task ExtraArchiveFilesAreNotDiscardedByDeleteOption()
    {
        string folder=TestMediaFixture.NewFolder();TestMediaFixture.Seed(folder,Action.Zip_To_Bin,"keep");string path=Path.Combine(folder,"song.zip");
        using(var zip=ZipFile.Open(path,ZipArchiveMode.Update))using(var e=new StreamWriter(zip.CreateEntry("notes.txt").Open()))e.Write("keep me");
        string before=FileTransaction.Hash(path);Assert.Equal(1,(await Run(folder,Action.Zip_To_AudioG,delete:true)).Failed);Assert.Equal(before,FileTransaction.Hash(path));
    }
    [Fact] public async Task AutomaticCueOutputIsGeneratedAfterBinConversion()
    {
        string folder=TestMediaFixture.NewFolder();TestMediaFixture.Seed(folder,Action.AudioG_To_Bin,"keep");
        var result=await new ConversionService().RunAsync(new(Action.AudioG_To_Bin,folder,Options() with{AutoCreateCueText=true}));
        Assert.Equal(0,result.Failed);Assert.Single(Directory.GetFiles(folder,"*.cue"));Assert.Single(Directory.GetFiles(folder,"*.txt"));
    }
    [Fact] public async Task RecursionCanBeDisabledAndRecoveryDirectoriesAreExcluded()
    {
        string folder=TestMediaFixture.NewFolder();TestMediaFixture.Seed(folder,Action.AudioG_To_Bin,"keep");TestMediaFixture.Seed(Path.Combine(folder,"child"),Action.AudioG_To_Bin,"keep");
        TestMediaFixture.Seed(Path.Combine(folder,".karaoke-job-recovery"),Action.AudioG_To_Bin,"keep");
        var result=await new ConversionService().RunAsync(new(Action.AudioG_To_Bin,folder,Options() with{RecurseFolders=false}));
        Assert.Equal(1,result.Succeeded);
        result=await new ConversionService().RunAsync(new(Action.AudioG_To_Bin,folder,Options()),OverwritePolicy.Skip);
        Assert.Equal(1,result.Succeeded);Assert.Equal(1,result.Skipped);Assert.False(File.Exists(Path.Combine(folder,".karaoke-job-recovery","song.bin")));
    }    [Theory]
    [InlineData(AudioEncoder.MP3)]
    [InlineData(AudioEncoder.OGG)]
    public async Task CompressedAudioConvertsBackToBinWithoutLosingGraphics(AudioEncoder encoder)
    {
        string folder=TestMediaFixture.NewFolder();TestMediaFixture.Seed(folder,Action.Bin_To_AudioG,"keep");
        var options=Options() with{AudioEncoder=encoder};
        Assert.Equal(1,(await new ConversionService().RunAsync(new(Action.Bin_To_AudioG,folder,options))).Succeeded);
        string before=FileTransaction.Hash(Path.Combine(folder,"song.cdg"));
        var result=await new ConversionService().RunAsync(new(Action.AudioG_To_Bin,folder,options),OverwritePolicy.Replace);
        Assert.True(result.Failed==0,string.Join(";",result.Files.Select(x=>x.Message)));
        await BinConverter.SplitAsync(Path.Combine(folder,"song.bin"),Path.Combine(folder,"check.wav"),Path.Combine(folder,"check.cdg"),default);
        Assert.Equal(before,FileTransaction.Hash(Path.Combine(folder,"check.cdg")));
    }    private sealed class Immediate<T>(Action<T> callback):IProgress<T>{public void Report(T value)=>callback(value);}
}
