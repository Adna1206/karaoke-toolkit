using KaraokeToolkit.Core.Execution;
using KaraokeToolkit.Core.Recovery;
using KaraokeToolkit.Core.DiscRipping;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.App.Features.Main;
using KaraokeToolkit.Core.FileOperations;

namespace KaraokeToolkit.Tests.FileOperations;

public class FlattenFolderLayerTests
{
    [Fact]
    public async Task AllLayersRemovesEmptyReadOnlyFoldersOnWindows()
    {
        if (!OperatingSystem.IsWindows())
            return;

        string root = TestMediaFixture.NewFolder();
        string parent = Path.Combine(root, "Batch");
        string album = Path.Combine(parent, "Album");
        Directory.CreateDirectory(album);
        File.SetAttributes(parent, File.GetAttributes(parent) | FileAttributes.ReadOnly);
        File.SetAttributes(album, File.GetAttributes(album) | FileAttributes.ReadOnly);

        var result = await new FlattenFolderLayer().RunAsync([root], scope: FolderFlattenScope.AllLayers);

        Assert.Equal(2, result.Succeeded);
        Assert.False(Directory.Exists(parent));
    }

    [Fact]
    public async Task AllLayersReportsLockedCleanupAndContinuesWithOtherFolders()
    {
        string root = TestMediaFixture.NewFolder();
        string parent = Path.Combine(root, "Locked");
        string child = Path.Combine(parent, "Empty");
        string sibling = Path.Combine(root, "Other");
        Directory.CreateDirectory(child);
        Directory.CreateDirectory(sibling);
        using var lease = FileTransaction.AcquireLease(parent);

        var result = await new FlattenFolderLayer().RunAsync([root], scope: FolderFlattenScope.AllLayers);

        Assert.Equal(1, result.Failed);
        Assert.True(Directory.Exists(child));
        Assert.False(Directory.Exists(sibling));
    }

    [Fact]
    public async Task SelectedFoldersLoseOneLayerWhileNestedFoldersAndRootFilesStayIntact()
    {
        string first = TestMediaFixture.NewFolder(), second = TestMediaFixture.NewFolder();
        File.WriteAllText(Path.Combine(first, "already.txt"), "untouched");
        Directory.CreateDirectory(Path.Combine(first, "One", "Album", "Disc"));
        File.WriteAllText(Path.Combine(first, "One", "song.mp3"), "audio");
        File.WriteAllText(Path.Combine(first, "One", "song.cdg"), "graphics");
        File.WriteAllText(Path.Combine(first, "One", "Album", "Disc", "track.txt"), "deep");
        Directory.CreateDirectory(Path.Combine(first, "Empty"));
        Directory.CreateDirectory(Path.Combine(second, "Two"));
        File.WriteAllText(Path.Combine(second, "Two", "other.zip"), "archive");

        var model = new MainViewModel(Path.Combine(TestMediaFixture.NewFolder(), "options.xmls"));
        model.SelectedOperation = model.Operations.Single(operation => operation.FlattenScope == FolderFlattenScope.OneLayer);
        Assert.False(model.CanChooseFiles);
        model.SetInputs([first, second]);
        await model.StartCommand.ExecuteAsync(null);

        Assert.Equal("File Organization Report", model.LastReport!.ReportType);
        Assert.DoesNotContain(model.LastReport.Entries, entry => entry.Status == "Failed");
        Assert.True(File.Exists(Path.Combine(first, "song.mp3")));
        Assert.True(File.Exists(Path.Combine(first, "song.cdg")));
        Assert.True(File.Exists(Path.Combine(first, "Album", "Disc", "track.txt")));
        Assert.True(File.Exists(Path.Combine(first, "already.txt")));
        Assert.True(File.Exists(Path.Combine(second, "other.zip")));
        Assert.False(Directory.Exists(Path.Combine(first, "One")));
        Assert.False(Directory.Exists(Path.Combine(first, "Empty")));
        Assert.False(Directory.Exists(Path.Combine(second, "Two")));
        Assert.False(model.CanStart);
    }

    [Fact]
    public async Task NameCollisionsKeepTheirSourcesAndDoNotDeleteNonemptyChild()
    {
        string root = TestMediaFixture.NewFolder();
        File.WriteAllText(Path.Combine(root, "song.mp3"), "original");
        Directory.CreateDirectory(Path.Combine(root, "One"));
        File.WriteAllText(Path.Combine(root, "One", "song.mp3"), "duplicate");
        File.WriteAllText(Path.Combine(root, "One", "song.cdg"), "graphics");
        Directory.CreateDirectory(Path.Combine(root, "Two"));
        File.WriteAllText(Path.Combine(root, "Two", "song.cdg"), "second graphics");

        var result = await new FlattenFolderLayer().RunAsync([root]);

        Assert.Equal("original", File.ReadAllText(Path.Combine(root, "song.mp3")));
        Assert.Equal("duplicate", File.ReadAllText(Path.Combine(root, "One", "song.mp3")));
        Assert.Equal("graphics", File.ReadAllText(Path.Combine(root, "song.cdg")));
        Assert.Equal("second graphics", File.ReadAllText(Path.Combine(root, "Two", "song.cdg")));
        Assert.Equal(1, result.Succeeded);
        Assert.Equal(4, result.Skipped);
        Assert.Equal(0, result.Failed);
    }

    [Fact]
    public async Task NestedSelectionsAreProcessedOnceAndFileSelectionsAreRejected()
    {
        string root = TestMediaFixture.NewFolder();
        string child = Path.Combine(root, "Layer");
        Directory.CreateDirectory(Path.Combine(child, "Deep"));
        File.WriteAllText(Path.Combine(child, "Deep", "track.txt"), "track");
        string file = Path.Combine(root, "root.txt");
        File.WriteAllText(file, "root");
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => new FlattenFolderLayer().RunAsync([file]));

        var result = await new FlattenFolderLayer().RunAsync([root, child]);

        Assert.Equal(0, result.Failed);
        Assert.True(File.Exists(Path.Combine(root, "Deep", "track.txt")));
        Assert.False(Directory.Exists(child));
        Assert.True(File.Exists(file));
    }

    [Fact]
    public async Task ProtectedChildFolderIsLeftInPlace()
    {
        string root = TestMediaFixture.NewFolder();
        Directory.CreateDirectory(Path.Combine(root, "Damaged"));
        File.WriteAllText(Path.Combine(root, "Damaged", "song.mp3"), "keep");
        var result = await new FlattenFolderLayer().RunAsync([root]);
        Assert.Equal(0, result.Succeeded);
        Assert.Equal(2, result.Skipped);
        Assert.True(File.Exists(Path.Combine(root, "Damaged", "song.mp3")));
    }

    [Fact]
    public async Task AllLayersMoveIntoEachSelectedRootAndLeaveItsParentUntouched()
    {
        string parent = TestMediaFixture.NewFolder(), second = TestMediaFixture.NewFolder();
        string root = Path.Combine(parent, "B"), deep = Path.Combine(root, "Album", "Disc", "Tracks");
        Directory.CreateDirectory(deep);
        Directory.CreateDirectory(Path.Combine(root, "Album", "Empty", "Nested"));
        File.WriteAllText(Path.Combine(parent, "parent.txt"), "parent");
        File.WriteAllText(Path.Combine(root, "root.txt"), "root");
        File.WriteAllText(Path.Combine(root, "Album", "cover.txt"), "cover");
        File.WriteAllText(Path.Combine(deep, "song.mp3"), "audio");
        File.WriteAllText(Path.Combine(deep, "song.cdg"), "graphics");
        Directory.CreateDirectory(Path.Combine(second, "One", "Two"));
        File.WriteAllText(Path.Combine(second, "One", "Two", "other.zip"), "archive");

        var model = new MainViewModel(Path.Combine(TestMediaFixture.NewFolder(), "options.xmls"));
        model.SelectedOperation = model.Operations.Single(operation => operation.FlattenScope == FolderFlattenScope.AllLayers);
        Assert.False(model.CanChooseFiles);
        model.SetInputs([root, deep, second]);
        bool? processingViewDuringRecovery = null;
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(model.ShowRecoveryCheckProgress) && model.ShowRecoveryCheckProgress)
                processingViewDuringRecovery = model.ShowProgress;
        };
        await model.StartCommand.ExecuteAsync(null);

        Assert.Equal(false, processingViewDuringRecovery);
        Assert.DoesNotContain(model.LastReport!.Entries, entry => entry.Status is "Failed" or "Skipped");
        Assert.Equal("audio", File.ReadAllText(Path.Combine(root, "song.mp3")));
        Assert.Equal("graphics", File.ReadAllText(Path.Combine(root, "song.cdg")));
        Assert.Equal("cover", File.ReadAllText(Path.Combine(root, "cover.txt")));
        Assert.Equal("root", File.ReadAllText(Path.Combine(root, "root.txt")));
        Assert.Equal("parent", File.ReadAllText(Path.Combine(parent, "parent.txt")));
        Assert.False(File.Exists(Path.Combine(parent, "song.mp3")));
        Assert.True(File.Exists(Path.Combine(second, "other.zip")));
        Assert.Empty(Directory.GetDirectories(root));
        Assert.Empty(Directory.GetDirectories(second));
        Assert.False(model.CanStart);
    }

    [Fact]
    public async Task AllLayersKeepCollisionsInTheirOriginalDeepFolders()
    {
        string root = TestMediaFixture.NewFolder();
        string deep = Path.Combine(root, "Album", "Disc");
        Directory.CreateDirectory(deep);
        File.WriteAllText(Path.Combine(root, "duplicate.txt"), "original");
        File.WriteAllText(Path.Combine(deep, "duplicate.txt"), "duplicate");
        File.WriteAllText(Path.Combine(deep, "unique.txt"), "unique");

        var result = await new FlattenFolderLayer().RunAsync([root], scope: FolderFlattenScope.AllLayers);

        Assert.Equal(0, result.Failed);
        Assert.Equal("original", File.ReadAllText(Path.Combine(root, "duplicate.txt")));
        Assert.Equal("duplicate", File.ReadAllText(Path.Combine(deep, "duplicate.txt")));
        Assert.Equal("unique", File.ReadAllText(Path.Combine(root, "unique.txt")));
        Assert.Equal(1, result.Succeeded);
        Assert.Equal(3, result.Skipped);
    }

    [Fact]
    public async Task AllLayersDoNotEnterProtectedDescendantFolders()
    {
        string root = TestMediaFixture.NewFolder();
        string damaged = Path.Combine(root, "Album", "Damaged", "Batch");
        Directory.CreateDirectory(damaged);
        File.WriteAllText(Path.Combine(damaged, "song.mp3"), "keep");
        File.WriteAllText(Path.Combine(root, "Album", "other.txt"), "move");

        var result = await new FlattenFolderLayer().RunAsync([root], scope: FolderFlattenScope.AllLayers);

        Assert.Equal(0, result.Failed);
        Assert.True(File.Exists(Path.Combine(damaged, "song.mp3")));
        Assert.False(File.Exists(Path.Combine(root, "song.mp3")));
        Assert.True(File.Exists(Path.Combine(root, "other.txt")));
        Assert.Equal(2, result.Skipped);
    }

    [Fact]
    public async Task CancellationKeepsCompletedMovesAndRemainingSourceFolders()
    {
        string root = TestMediaFixture.NewFolder();
        string deep = Path.Combine(root, "Album", "Disc");
        Directory.CreateDirectory(deep);
        File.WriteAllText(Path.Combine(deep, "a.txt"), "a");
        File.WriteAllText(Path.Combine(deep, "b.txt"), "b");
        using var cancellation = new CancellationTokenSource();
        var progress = new ImmediateProgress(value => { if (value.Completed == 1) cancellation.Cancel(); });

        var result = await new FlattenFolderLayer().RunAsync([root], progress, cancellation.Token, FolderFlattenScope.AllLayers);

        Assert.True(result.Cancelled);
        Assert.True(File.Exists(Path.Combine(root, "a.txt")));
        Assert.True(File.Exists(Path.Combine(deep, "b.txt")));
        Assert.True(Directory.Exists(deep));
    }

    [Fact]
    public async Task StandaloneRecoveryResumesAfterVerifiedMoveWithoutReceipt()
    {
        string root = TestMediaFixture.NewFolder();
        string child = Directory.CreateDirectory(Path.Combine(root, "Album")).FullName;
        string source = Path.Combine(child, "track.zip");
        string target = Path.Combine(root, "track.zip");
        File.WriteAllText(source, "track");
        var store = new RecoveryStore(TestMediaFixture.NewFolder());
        string journal;
        using (var session = new WorkflowSession(store,
            new WorkflowData
            {
                Kind = RecoveryKind.FileMoves,
                OperationId = OperationId.FlattenOneLayer,
                Roots = [root],
                Moves = [new() { Items = [WorkflowMoves.Move(source, target)] },
                    new() { Items = [new(child, null, "empty", true)] }]
            }))
        {
            journal = session.Journal;
            File.Move(source, target);
        }

        Assert.Single(store.Find());
        var result = await new OperationExecutionService(store).ResumeAsync(journal);

        Assert.False(result.HasIssues);
        Assert.Equal("track", File.ReadAllText(target));
        Assert.False(Directory.Exists(child));
        Assert.Empty(store.Find());
    }

    private sealed class ImmediateProgress(System.Action<KaraokeToolkit.Core.Conversion.ConversionProgress> report)
        : IProgress<KaraokeToolkit.Core.Conversion.ConversionProgress>
    {
        public void Report(KaraokeToolkit.Core.Conversion.ConversionProgress value) => report(value);
    }
}
