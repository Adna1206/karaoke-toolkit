using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Recovery;
using KaraokeToolkit.Core.Settings;
using System.Diagnostics;
using System.Text.Json;
using KaraokeToolkit.Core;
namespace KaraokeToolkit.Core.Tests.Recovery;

public class FileTransactionTests
{

    [Fact] public async Task ChangedSourceIsNotDeleted()
    {
        string folder=TestMediaFixture.NewFolder(),source=Path.Combine(folder,"input");File.WriteAllText(source,"first");
        using var transaction=new FileTransaction(folder);await transaction.CopyInputAsync(source,default);
        string output=Path.Combine(transaction.Workspace,"output");File.WriteAllText(output,"result");File.WriteAllText(source,"changed");
        Assert.Throws<IOException>(()=>transaction.Commit([output],[source],OverwritePolicy.Replace,default));Assert.Equal("changed",File.ReadAllText(source));Assert.False(File.Exists(Path.Combine(folder,"output")));
    }
    [Fact] public async Task FailedCommitRollsBackOldOutputsAndSource()
    {
        string folder=TestMediaFixture.NewFolder(),source=Path.Combine(folder,"input"),old=Path.Combine(folder,"output");File.WriteAllText(source,"source");File.WriteAllText(old,"old");
        using var transaction=new FileTransaction(folder);await transaction.CopyInputAsync(source,default);string output=Path.Combine(transaction.Workspace,"output");File.WriteAllText(output,"new");
        // Deny moving the staged output: the failure happens after existing targets were backed up.
        using(var held=new FileStream(output,FileMode.Open,FileAccess.Read,FileShare.Read))
            Assert.Throws<IOException>(()=>transaction.Commit([output],[source],OverwritePolicy.Replace,default));
        Assert.Equal("source",File.ReadAllText(source));Assert.Equal("old",File.ReadAllText(old));
    }
    [Fact] public void OverlappingTransactionsInSameFolderAreRejected()
    {
        string folder=TestMediaFixture.NewFolder();using var transaction=new FileTransaction(folder);
        Assert.Throws<IOException>(()=>new FileTransaction(folder));
    }
}
