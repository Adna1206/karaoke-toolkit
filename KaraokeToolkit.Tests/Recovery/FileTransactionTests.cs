using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Recovery;

namespace KaraokeToolkit.Tests.Recovery;

public class FileTransactionTests
{

    [Theory]
    [InlineData(RecoveryVerificationMode.Sha256)]
    [InlineData(RecoveryVerificationMode.XxHash128)]
    [InlineData(RecoveryVerificationMode.MetadataOnly)]
    public async Task ChangedSourceIsNotDeleted(RecoveryVerificationMode mode)
    {
        string folder = TestMediaFixture.NewFolder();
        string source = Path.Combine(folder, "input");
        File.WriteAllText(source, "first");
        using var transaction = new FileTransaction(folder, mode);
        await transaction.CopyInputAsync(source, default);
        string stagedOutput = Path.Combine(transaction.Workspace, "output");
        File.WriteAllText(stagedOutput, "result");

        File.WriteAllText(source, "changed");
        Assert.Throws<IOException>(() =>
            transaction.Commit([stagedOutput], [source], OverwritePolicy.Replace, default));

        Assert.Equal("changed", File.ReadAllText(source));
        Assert.False(File.Exists(Path.Combine(folder, "output")));
    }

    [Fact]
    public async Task FailedCommitRollsBackOldOutputsAndSource()
    {
        string folder = TestMediaFixture.NewFolder();
        string source = Path.Combine(folder, "input");
        string existingOutput = Path.Combine(folder, "output");
        File.WriteAllText(source, "source");
        File.WriteAllText(existingOutput, "old");
        using var transaction = new FileTransaction(folder);
        await transaction.CopyInputAsync(source, default);
        string stagedOutput = Path.Combine(transaction.Workspace, "output");
        File.WriteAllText(stagedOutput, "new");

        // Deny moving the staged output: the failure happens after existing targets were backed up.
        using (var held = new FileStream(stagedOutput, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.Throws<IOException>(() =>
                transaction.Commit([stagedOutput], [source], OverwritePolicy.Replace, default));

        Assert.Equal("source", File.ReadAllText(source));
        Assert.Equal("old", File.ReadAllText(existingOutput));
    }

    [Fact]
    public void OverlappingTransactionsInSameFolderAreRejected()
    {
        string folder = TestMediaFixture.NewFolder();
        using var transaction = new FileTransaction(folder);

        Assert.Throws<IOException>(() => new FileTransaction(folder));
    }
}
