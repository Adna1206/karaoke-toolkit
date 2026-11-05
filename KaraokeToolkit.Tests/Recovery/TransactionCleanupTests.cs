using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Execution;
using KaraokeToolkit.Core.Recovery;

namespace KaraokeToolkit.Tests.Recovery;

public class TransactionCleanupTests
{
    [Fact]
    public async Task ReadOnlyOriginalIsRemovedOnlyAfterVerifiedPublication()
    {
        string folder = TestMediaFixture.NewFolder();
        string source = Path.Combine(folder, "source");
        File.WriteAllText(source, "original");
        File.SetAttributes(source, FileAttributes.ReadOnly);
        string workspace;

        using (var transaction = new FileTransaction(folder))
        {
            workspace = transaction.Workspace;
            await transaction.CopyInputAsync(source, default);
            string stagedOutput = Path.Combine(workspace, "output");
            File.WriteAllText(stagedOutput, "published");

            transaction.Commit([stagedOutput], [source], OverwritePolicy.Fail, default);

            Assert.Equal("published", File.ReadAllText(Path.Combine(folder, "output")));
            Assert.True(File.Exists(Path.Combine(workspace, "backups", "0")));
        }

        Assert.False(Directory.Exists(workspace));
        Assert.False(File.Exists(source));
        Assert.Equal("published", File.ReadAllText(Path.Combine(folder, "output")));
    }

    [Fact]
    public async Task AbandonedReadOnlyStagingDoesNotChangeOriginalAttributes()
    {
        string folder = TestMediaFixture.NewFolder();
        string source = Path.Combine(folder, "source");
        File.WriteAllText(source, "original");
        File.SetAttributes(source, FileAttributes.ReadOnly);
        string workspace;

        using (var transaction = new FileTransaction(folder))
        {
            workspace = transaction.Workspace;
            string stagedInput = await transaction.CopyInputAsync(source, default);
            File.SetAttributes(stagedInput, FileAttributes.ReadOnly);
        }

        Assert.False(Directory.Exists(workspace));
        Assert.Equal("original", File.ReadAllText(source));
        Assert.True((File.GetAttributes(source) & FileAttributes.ReadOnly) != 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LockedWorkspaceKeepsResumableRunUntilCleanupCanSucceed(bool committed)
    {
        if (!OperatingSystem.IsWindows()) return; // Unix permits unlinking an open file.
        string folder = TestMediaFixture.NewFolder();
        string source = Path.Combine(folder, "source.zip");
        File.WriteAllText(source, "original");
        var store = new RecoveryStore(TestMediaFixture.NewFolder());
        string workspace;
        FileStream? held = null;
        var previous = RunSession.Current.Value;
        try
        {
            using var run = new RunSession(store, new(OperationId.ZipToAudioG, folder,
                new() { AutoCreateCueText = false }), OverwritePolicy.Fail, [source]);
            RunSession.Current.Value = run;
            run.Begin(source);
            var transaction = new FileTransaction(folder);
            workspace = transaction.Workspace;
            string stagedInput = await transaction.CopyInputAsync(source, default);
            string output = Path.Combine(workspace, "output");
            File.WriteAllText(output, "published");
            if (committed) transaction.Commit([output], [source], OverwritePolicy.Fail, default);
            held = new FileStream(committed ? Path.Combine(workspace, "backups", "0") : stagedInput,
                FileMode.Open, FileAccess.Read, FileShare.Read);

            var error = Assert.Throws<IOException>(() => transaction.Dispose());
            Assert.Contains(workspace, error.Message);
            Assert.Contains("cleanup", error.Message);
            Assert.Equal(committed, File.Exists(Path.Combine(workspace, "recovery.json")));
            Assert.Throws<IOException>(() => run.ThrowIfUnresolved());
            if (committed)
            {
                Assert.Equal("published", File.ReadAllText(Path.Combine(folder, "output")));
                Assert.Throws<IOException>(() => new FileTransaction(folder));
            }
        }
        finally
        {
            RunSession.Current.Value = previous;
            held?.Dispose();
        }

        var job = Assert.Single(store.Find());
        using (var resumed = new RunSession(store, job.JournalPath))
        {
            resumed.RecoverPending();
            Assert.False(Directory.Exists(workspace));
            resumed.Finish();
        }
        Assert.Empty(store.Find());
        if (committed) Assert.Equal("published", File.ReadAllText(Path.Combine(folder, "output")));
        else Assert.Equal("original", File.ReadAllText(source));
    }

    [Fact]
    public void CleanupRefusesLinksBeforeChangingAnyFiles()
    {
        string folder = TestMediaFixture.NewFolder();
        string outside = TestMediaFixture.NewFolder();
        string protectedFile = Path.Combine(outside, "original");
        File.WriteAllText(protectedFile, "keep");
        File.SetAttributes(protectedFile, FileAttributes.ReadOnly);
        string workspace = Path.Combine(folder, ".karaoke-job-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        string journal = Path.Combine(workspace, "recovery.json");
        File.WriteAllText(journal, "keep journal");
        try { Directory.CreateSymbolicLink(Path.Combine(workspace, "linked"), outside); }
        catch (UnauthorizedAccessException) { return; } // Requires symlink permission on Windows.

        Assert.Throws<IOException>(() => RecoveryWorkspaceCleanup.Delete(workspace));

        Assert.Equal("keep journal", File.ReadAllText(journal));
        Assert.Equal("keep", File.ReadAllText(protectedFile));
        Assert.True((File.GetAttributes(protectedFile) & FileAttributes.ReadOnly) != 0);
    }

    [Theory]
    [InlineData(0x9000001au, true)]
    [InlineData(0x9000601au, true)]
    [InlineData(0x9000f01au, true)]
    [InlineData(0xa0000003u, false)] // Junction / mounted directory.
    [InlineData(0xa000000cu, false)] // Symbolic link.
    [InlineData(0x80000018u, false)] // Unrecognized provider.
    public void OnlyKnownCloudPlaceholderTagsAreAllowed(uint tag, bool expected)
        => Assert.Equal(expected, RecoveryWorkspaceCleanup.IsCloudTag(tag));
}
