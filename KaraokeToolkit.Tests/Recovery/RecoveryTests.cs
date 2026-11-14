using KaraokeToolkit.Core.Execution;
using KaraokeToolkit.App.Features.Settings;
using KaraokeToolkit.App.Features.Main;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Recovery;
using KaraokeToolkit.Core;
using Action = KaraokeToolkit.Core.Execution.OperationId;

namespace KaraokeToolkit.Tests.Recovery;

/// <summary>Exercises interrupted transaction states, durable conversion plans and recovery discovery.</summary>
/// <remarks>Most cases construct exact journal/file states; the process test separately kills a real worker.
/// Reconstructed states verify recovery contracts rather than simulating physical power loss.</remarks>
public class RecoveryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TransactionRecoveryReadsLegacySha256AndExplicitXxHash128(bool xxHash)
    {
        string root = TestMediaFixture.NewFolder();
        string workspace = Directory.CreateDirectory(Path.Combine(root, ".karaoke-job-test")).FullName;
        string staged = Path.Combine(workspace, "track.zip");
        File.WriteAllText(staged, "recovery payload");
        string mode = xxHash ? RecoveryFingerprint.XxHash128 : RecoveryFingerprint.Sha256;
        string fingerprint = RecoveryFingerprint.Hash(staged, mode);
        string json = System.Text.Json.JsonSerializer.Serialize(new TransactionJournal
        {
            VerificationMode = mode,
            Outputs = [new("track.zip", fingerprint)]
        });
        if (!xxHash) json = json.Replace("\"VerificationMode\":\"Sha256\",", "");
        File.WriteAllText(Path.Combine(workspace, "recovery.json"), json);

        Assert.Single(FileTransactionRecovery.Recover(workspace)!);
        Assert.Equal("recovery payload", File.ReadAllText(Path.Combine(root, "track.zip")));
    }

    /// <summary>Kills a worker after staging with one prior failure recorded, then resumes only unfinished work.</summary>
    /// <remarks>The ready-file handshake establishes the crash boundary; active leases hide the job until exit.</remarks>
    [Fact]
    public async Task KilledConversionProcessIsDiscoveredAndResumed()
    {
        string folder = TestMediaFixture.NewFolder(), index = TestMediaFixture.NewFolder();
        TestMediaFixture.Seed(folder, Action.ZipToBin, "keep");
        File.WriteAllText(Path.Combine(folder, "bad.zip"), "invalid archive");
        var start = new System.Diagnostics.ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add(typeof(RecoveryTests).Assembly.Location);
        start.ArgumentList.Add("--recovery-crash-worker");
        start.ArgumentList.Add(folder);
        start.ArgumentList.Add(index);
        using var child = System.Diagnostics.Process.Start(start)!;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            while (!File.Exists(Path.Combine(folder, "crash-ready")))
            {
                Assert.False(child.HasExited, "Crash worker exited before staging completed.");
                await Task.Delay(50, timeout.Token);
            }
            Assert.Empty(new RecoveryStore(index).Find());
            child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync(timeout.Token);
            var store = new RecoveryStore(index);
            var job = Assert.Single(store.Find());
            Assert.Equal(1, job.Completed); // The failed first source was durably recorded.
            var result = await new ConversionService(store).ResumeAsync(job);
            Assert.Equal(1, result.Succeeded);
            Assert.Equal(1, result.Failed);
            Assert.True(File.Exists(Path.Combine(folder, "song.cdg")));
            Assert.True(File.Exists(Path.Combine(folder, "song.wav")));
            Assert.Empty(store.Find());
        }
        finally { if (!child.HasExited) { child.Kill(true); await child.WaitForExitAsync(); } }
    }

    /// <summary>Creates a publishing journal, original ZIP/graphics and staged replacement audio/graphics before any moves.</summary>
    private static (string Folder, string Workspace, TransactionJournal Journal) Stage()
    {
        string folder = TestMediaFixture.NewFolder();
        string workspace = Path.Combine(folder, ".karaoke-job-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(workspace, "backups"));
        File.WriteAllText(Path.Combine(folder, "source.zip"), "source");
        File.WriteAllText(Path.Combine(folder, "song.cdg"), "old graphics");
        File.WriteAllText(Path.Combine(workspace, "song.cdg"), "new graphics");
        File.WriteAllText(Path.Combine(workspace, "song.mp3"), "new audio");
        var journal = new TransactionJournal
        {
            Outputs = new[] { "song.cdg", "song.mp3" }.Select(n => new RecoveryOutput(n, FileTransaction.Hash(Path.Combine(workspace, n)))).ToArray(),
            Backups = new[] { "song.cdg", "source.zip" }.Select((n, i) => new RecoveryBackup(n, i.ToString(), FileTransaction.Hash(Path.Combine(folder, n)))).ToArray()
        };
        DurableJson.Write(Path.Combine(workspace, "recovery.json"), journal);
        return (folder, workspace, journal);
    }
    private static void MoveBackups(string folder, string workspace, TransactionJournal journal, int count)
    {
        foreach (var old in journal.Backups.Take(count))
            File.Move(Path.Combine(folder, old.Name), Path.Combine(workspace, "backups", old.Slot));
    }

    /// <summary>Completes publication from each represented move boundary and checks that repeating recovery is safe.</summary>
    /// <param name="backups">Number of originals already moved into their journaled backup slots.</param>
    /// <param name="outputs">Number of staged outputs already moved into their published paths.</param>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    [InlineData(2, 1)]
    [InlineData(2, 2)]
    public void EveryPublicationBoundaryRecoversAndCanRecoverAgain(int backups, int outputs)
    {
        var (folder, workspace, journal) = Stage();
        MoveBackups(folder, workspace, journal, backups);
        foreach (var output in journal.Outputs.Take(outputs))
            File.Move(Path.Combine(workspace, output.Name), Path.Combine(folder, output.Name));
        Assert.Equal(2, FileTransactionRecovery.Recover(workspace)!.Count);
        Assert.Equal(2, FileTransactionRecovery.Recover(workspace)!.Count);
        Assert.Equal("new graphics", File.ReadAllText(Path.Combine(folder, "song.cdg")));
        Assert.Equal("new audio", File.ReadAllText(Path.Combine(folder, "song.mp3")));
        Assert.False(File.Exists(Path.Combine(folder, "source.zip")));
        Assert.Equal("source", File.ReadAllText(Path.Combine(workspace, "backups", "1")));
    }
    /// <summary>Edits one staged, backed-up, published or unexpected file and requires recovery to leave the entire tree unchanged.</summary>
    [Theory]
    [InlineData("staged")]
    [InlineData("backup")]
    [InlineData("published")]
    [InlineData("new target")]
    public void ChangedFilesBlockRecoveryWithoutMovingAnything(string kind)
    {
        var (folder, workspace, journal) = Stage();
        MoveBackups(folder, workspace, journal, 2);
        string path = kind switch
        {
            "staged" => Path.Combine(workspace, "song.cdg"),
            "backup" => Path.Combine(workspace, "backups", "1"),
            _ => Path.Combine(folder, "song.cdg")
        };
        if (kind == "published")
            File.Move(Path.Combine(workspace, "song.cdg"), path);
        File.WriteAllText(path, "user changes");
        var before = Snapshot(folder);
        Assert.Throws<IOException>(() => FileTransactionRecovery.Recover(workspace));
        Assert.Equal(before, Snapshot(folder));
    }
    private static string[] Snapshot(string folder) => Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Order().Select(p => p + ":" + FileTransaction.Hash(p)).ToArray();

    [Fact]
    public void TraversalJournalIsRejected()
    {
        var (folder, workspace, journal) = Stage();
        journal.Outputs = [new("../outside", journal.Outputs[0].Hash)];
        DurableJson.Write(Path.Combine(workspace, "recovery.json"), journal);
        var before = Snapshot(folder);
        Assert.Throws<IOException>(() => FileTransactionRecovery.Recover(workspace));
        Assert.Equal(before, Snapshot(folder));
    }
    /// <summary>Reconstructs rollback interrupted after removing one new output; repeated recovery must restore originals.</summary>
    [Fact]
    public void InterruptedRollbackRestoresOriginalsIdempotently()
    {
        var (folder, workspace, journal) = Stage();
        MoveBackups(folder, workspace, journal, 2);
        File.Move(Path.Combine(workspace, "song.cdg"), Path.Combine(folder, "song.cdg"));
        journal.Phase = "RollingBack";
        DurableJson.Write(Path.Combine(workspace, "recovery.json"), journal);
        File.Delete(Path.Combine(folder, "song.cdg")); // Crash after undoing one published file.
        Assert.Null(FileTransactionRecovery.Recover(workspace));
        Assert.Null(FileTransactionRecovery.Recover(workspace));
        Assert.Equal("old graphics", File.ReadAllText(Path.Combine(folder, "song.cdg")));
        Assert.Equal("source", File.ReadAllText(Path.Combine(folder, "source.zip")));
    }
    [Fact]
    public async Task ResumeUsesSavedPlanAndDoesNotReprocessCompletedGroups()
    {
        string folder = TestMediaFixture.NewFolder();
        string index = TestMediaFixture.NewFolder();
        var store = new RecoveryStore(index);
        TestMediaFixture.Seed(folder, Action.ZipToBin, "keep");
        string source = Path.Combine(folder, "song.zip");
        string done = Path.Combine(folder, "already.zip");
        string journal;
        using (var run = new RunSession(store, new(Action.ZipToAudioG, folder, new()
        {
            AutoCreateCueText = false,
            DeleteOldFiles = false
        }), OverwritePolicy.Fail, [done, source]))
        {
            journal = run.Journal;
            run.Begin(done);
            run.Complete(new(done, ConversionStatus.Succeeded, [], "Already done"));
            Assert.Empty(store.Find()); // An active process owns the run.
            run.Begin(source);
        }
        Assert.Single(store.Find(folder));
        var job = Assert.Single(store.Find());
        Assert.Equal(1, job.Completed);
        var result = await new ConversionService(store).ResumeAsync(job);
        Assert.Equal(2, result.Succeeded);
        Assert.Equal(0, result.Failed);
        Assert.True(File.Exists(Path.Combine(folder, "song.cdg")));
        Assert.True(File.Exists(source));
        Assert.False(File.Exists(journal));
        Assert.Empty(store.Find());
    }
    [Fact]
    public async Task PublishedButUncheckpointedGroupIsRecordedWithoutRerunning()
    {
        var (folder, workspace, transaction) = Stage();
        var store = new RecoveryStore(TestMediaFixture.NewFolder());
        string source = Path.Combine(folder, "source.zip");
        using (var run = new RunSession(store, new(Action.ZipToAudioG, folder, new()
        {
            AutoCreateCueText = false
        }), OverwritePolicy.Fail, [source]))
        {
            run.Begin(source);
            run.Attach(workspace);
        }
        MoveBackups(folder, workspace, transaction, 2);
        File.Move(Path.Combine(workspace, "song.cdg"), Path.Combine(folder, "song.cdg"));
        var result = await new ConversionService(store).ResumeAsync(Assert.Single(store.Find()));
        Assert.Equal(1, result.Succeeded);
        Assert.Equal(0, result.Failed); // The fake ZIP would fail if conversion ran again.
        Assert.False(Directory.Exists(workspace));
        Assert.Empty(store.Find());
    }
    [Fact]
    public async Task LaterLeavesJobUntouchedAndStartDoesNotRunOverIt()
    {
        string folder = TestMediaFixture.NewFolder(), settings = Path.Combine(TestMediaFixture.NewFolder(), "options.xmls");
        var store = new RecoveryStore(Path.Combine(Path.GetDirectoryName(settings)!, "Recovery"));
        using (var run = new RunSession(store, new(Action.ZipToAudioG, folder, new()), OverwritePolicy.Fail, []))
        {
        }
        var before = Snapshot(folder);
        var model = new MainViewModel(settings) { FolderPath = folder, ConfirmRecoveryAsync = _ => Task.FromResult(false) };
        await model.StartCommand.ExecuteAsync(null);
        Assert.True(model.RecoveryAvailable);
        Assert.False(model.IsBusy);
        Assert.Equal(before, Snapshot(folder));
    }
    [Fact]
    public async Task RecoveryModeIsVisibleAndSavedActionIsUsed()
    {
        string folder = TestMediaFixture.NewFolder(), settings = Path.Combine(TestMediaFixture.NewFolder(), "options.xmls");
        var store = new RecoveryStore(Path.Combine(Path.GetDirectoryName(settings)!, "Recovery"));
        using (var run = new RunSession(store, new(Action.ZipToAudioG, folder, new()), OverwritePolicy.Fail, []))
        {
        }
        var model = new MainViewModel(settings) { ConfirmRecoveryAsync = _ => Task.FromResult(true) };
        bool observed = false;
        model.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(model.IsRecoveryMode) && model.IsRecoveryMode) observed = true; };
        Assert.True(await model.CheckForRecoveryAsync());
        Assert.True(observed);
        Assert.Contains("Recovery", model.RunTitle);
        Assert.Equal(Action.ZipToAudioG, model.SelectedOperation!.Action);
        Assert.False(model.RecoveryAvailable);
    }

    [Fact]
    public async Task InterruptedStagingRestartsOnlyThatGroup()
    {
        string folder = TestMediaFixture.NewFolder();
        TestMediaFixture.Seed(folder, Action.ZipToBin, "keep");
        var store = new RecoveryStore(TestMediaFixture.NewFolder());
        string workspace = Path.Combine(folder, ".karaoke-job-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        File.WriteAllText(Path.Combine(workspace, "partial.tmp"), "partial data");
        using (var run = new RunSession(store, new(Action.ZipToAudioG, folder, new()
        {
            AutoCreateCueText = false,
            DeleteOldFiles = false
        }), OverwritePolicy.Fail, [Path.Combine(folder, "song.zip")]))
        {
            run.Begin(Path.Combine(folder, "song.zip"));
            run.Attach(workspace);
        }
        var result = await new ConversionService(store).ResumeAsync(Assert.Single(store.Find()));
        Assert.Equal(1, result.Succeeded);
        Assert.False(Directory.Exists(workspace));
    }

    [Fact]
    public void CommittedPublicationSurvivesPartiallyDeletedBackupsButStillChecksOutputs()
    {
        var (folder, workspace, journal) = Stage();
        MoveBackups(folder, workspace, journal, journal.Backups.Length);
        FileTransactionRecovery.Recover(workspace);
        File.Delete(Path.Combine(workspace, "backups", "0"));

        Assert.Equal(2, FileTransactionRecovery.Recover(workspace)!.Count);
        File.Delete(Path.Combine(workspace, "backups", "1"));
        Assert.Equal(2, FileTransactionRecovery.Recover(workspace)!.Count);
        Assert.False(File.Exists(Path.Combine(folder, "source.zip")));

        string output = Path.Combine(folder, "song.mp3");
        File.WriteAllText(output, "changed after publication");
        Assert.Throws<IOException>(() => FileTransactionRecovery.Recover(workspace));
        Assert.Equal("changed after publication", File.ReadAllText(output));
        Assert.True(File.Exists(Path.Combine(workspace, "recovery.json")));
    }

    [Fact]
    public async Task CrashAfterCheckpointBeforeBackupCleanupKeepsCompletedOutputs()
    {
        var (folder, workspace, transaction) = Stage();
        MoveBackups(folder, workspace, transaction, 2);
        var outputs = FileTransactionRecovery.Recover(workspace)!;
        var store = new RecoveryStore(TestMediaFixture.NewFolder());
        using (var run = new RunSession(store, new(Action.ZipToAudioG, folder, new()
        {
            AutoCreateCueText = false
        }), OverwritePolicy.Fail, [Path.Combine(folder, "source.zip")]))
        {
            run.Begin(Path.Combine(folder, "source.zip"));
            run.Attach(workspace);
            run.Committed(outputs);
        }
        File.Delete(Path.Combine(workspace, "backups", "0")); // Cleanup was interrupted.
        var result = await new ConversionService(store).ResumeAsync(Assert.Single(store.Find()));
        Assert.Equal(1, result.Succeeded);
        Assert.Equal("new graphics", File.ReadAllText(Path.Combine(folder, "song.cdg")));
        Assert.False(Directory.Exists(workspace));
    }

    [Fact]
    public async Task AutomaticMetadataResumesAfterCompletedMedia()
    {
        string folder = TestMediaFixture.NewFolder();
        TestMediaFixture.Seed(folder, Action.AudioGToBin, "keep");
        var store = new RecoveryStore(TestMediaFixture.NewFolder());
        string source = Path.Combine(folder, "song.zip");
        using (var run = new RunSession(store, new(Action.ZipToAudioG, folder, new()
        {
            AutoCreateCueText = true
        }), OverwritePolicy.Fail, [source]))
        {
            run.Begin(source);
            run.Committed([Path.Combine(folder, "song.wav"), Path.Combine(folder, "song.cdg")]);
            run.Begin(folder, metadata: true);
        }
        var result = await new ConversionService(store).ResumeAsync(Assert.Single(store.Find()));
        Assert.Equal(1, result.Succeeded);
        Assert.Equal(0, result.Failed);
        Assert.Single(Directory.GetFiles(folder, "*.txt"));
    }

    [Fact]
    public void NewRunAndTransactionCannotBypassPendingRecovery()
    {
        var (folder, workspace, _) = Stage();
        var store = new RecoveryStore(TestMediaFixture.NewFolder());
        using (var run = new RunSession(store, new(Action.ZipToAudioG, folder, new()), OverwritePolicy.Fail, []))
        {
        }
        Assert.Throws<IOException>(() => new RunSession(store, new(Action.ZipToAudioG, folder, new()), OverwritePolicy.Fail, []));
        Assert.Throws<IOException>(() => new FileTransaction(folder));
        Assert.True(Directory.Exists(workspace));
    }

    [Fact]
    public async Task FailedRecoveryRemainsDiscoverableAndDoesNotLoseBackups()
    {
        var (folder, workspace, transaction) = Stage();
        var store = new RecoveryStore(TestMediaFixture.NewFolder());
        using (var run = new RunSession(store, new(Action.ZipToAudioG, folder, new()), OverwritePolicy.Fail, [Path.Combine(folder, "source.zip")]))
        {
            run.Begin(Path.Combine(folder, "source.zip"));
            run.Attach(workspace);
        }
        MoveBackups(folder, workspace, transaction, 2);
        File.WriteAllText(Path.Combine(folder, "song.mp3"), "unrelated audio");
        var before = Snapshot(folder);
        await Assert.ThrowsAsync<IOException>(() => new ConversionService(store).ResumeAsync(Assert.Single(store.Find())));
        Assert.Equal(before, Snapshot(folder));
        Assert.Single(store.Find());
    }

    [Fact]
    public async Task FolderDiscoveryWorksWithoutStartupIndex()
    {
        string folder = TestMediaFixture.NewFolder();
        var store = new RecoveryStore(TestMediaFixture.NewFolder());
        using (var run = new RunSession(store, new(Action.ZipToAudioG, folder, new()), OverwritePolicy.Fail, []))
        {
        }
        foreach (string index in Directory.GetFiles(store.IndexDirectory))
            File.Delete(index);
        Assert.Empty(store.Find());
        var job = Assert.Single(store.Find(folder));
        await new ConversionService(store).ResumeAsync(job);
        Assert.Empty(store.Find(folder));
    }

    [Fact]
    public void DamagedJournalIsVisibleForInspection()
    {
        string folder = TestMediaFixture.NewFolder();
        var store = new RecoveryStore(TestMediaFixture.NewFolder());
        string journal;
        using (var run = new RunSession(store, new(Action.ZipToAudioG, folder, new()), OverwritePolicy.Fail, []))
            journal = run.Journal;
        File.WriteAllText(journal, "not json");
        Assert.NotNull(Assert.Single(store.Find()).Problem);
        Assert.NotNull(Assert.Single(store.Find(folder)).Problem);
        Assert.ThrowsAny<Exception>(() => new RunSession(store, new(Action.ZipToAudioG, folder, new()), OverwritePolicy.Fail, []));
        Assert.Equal("not json", File.ReadAllText(journal));
    }

    [Fact]
    public void ReappearingSourceBlocksRecoveryBeforeAnyMoves()
    {
        var (folder, workspace, transaction) = Stage();
        MoveBackups(folder, workspace, transaction, 2);
        File.WriteAllText(Path.Combine(folder, "source.zip"), "replacement source");
        var before = Snapshot(folder);
        Assert.Throws<IOException>(() => FileTransactionRecovery.Recover(workspace));
        Assert.Equal(before, Snapshot(folder));
    }
}
