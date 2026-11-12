using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Recovery;
using KaraokeToolkit.Core.Settings;
using System.Diagnostics;
using System.Text.Json;
using KaraokeToolkit.Core;
namespace KaraokeToolkit.Core.Tests.Settings;

public class SettingsReliabilityTests
{

    [Fact] public void AtomicSettingsKeepPreviousVersionAndUnknownEntries()
    {
        string folder=TestMediaFixture.NewFolder(),path=Path.Combine(folder,"options.xmls");var store=new SettingsStore(path,true);
        store.Save(new(){AudioBitrate=128},OverwritePolicy.Skip);string previous=File.ReadAllText(path);
        store.Save(new(){AudioBitrate=192,RenameBins=false},OverwritePolicy.Replace);
        Assert.Equal(previous,File.ReadAllText(path+".bak"));Assert.Equal(192,store.Load().AudioBitrate);Assert.False(store.Load().RenameBins);Assert.Equal(OverwritePolicy.Replace,store.LoadOverwritePolicy());Assert.Empty(Directory.GetFiles(folder,"*.tmp"));
    }
    [Fact] public async Task ConcurrentSettingsWritersLeaveCompleteDocument()
    {
        string folder=TestMediaFixture.NewFolder(),path=Path.Combine(folder,"options.xmls");
        await Task.WhenAll(Enumerable.Range(0,8).Select(i=>Task.Run(()=>new SettingsStore(path,true).Save(new(){AudioBitrate=64+i}))));
        Assert.InRange(new SettingsStore(path).Load().AudioBitrate,64,71);Assert.Empty(Directory.GetFiles(folder,"*.tmp"));
    }
    [Fact] public void SettingsWriteFailureKeepsPriorFile()
    {
        string folder=TestMediaFixture.NewFolder(),path=Path.Combine(folder,"options.xmls");var store=new SettingsStore(path,true);store.Save(new());string before=File.ReadAllText(path);
        using(var held=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))Assert.Throws<IOException>(()=>store.Save(new(){AudioBitrate=192}));
        Assert.Equal(before,File.ReadAllText(path));Assert.Empty(Directory.GetFiles(folder,"*.tmp"));
    }

}
