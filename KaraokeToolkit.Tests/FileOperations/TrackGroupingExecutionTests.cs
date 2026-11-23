using System.Text.Json.Nodes;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Execution;
using KaraokeToolkit.Core.FileOperations;
using KaraokeToolkit.Core.Recovery;
using KaraokeToolkit.Tests.Support;

namespace KaraokeToolkit.Tests.FileOperations;

public class TrackGroupingExecutionTests
{
    [Theory]
    [InlineData(false, FileCheckTiming.BeforeItem)]
    [InlineData(true, FileCheckTiming.BeforeItem)]
    [InlineData(false, FileCheckTiming.BeforeBatch)]
    [InlineData(true, FileCheckTiming.BeforeBatch)]
    public async Task ApprovedGroupingSkipsBlockedGroupsAndIgnoresNewFiles(bool move, FileCheckTiming timing)
    {
        string root = TestMediaFixture.NewFolder();
        string output = Path.Combine(root, "output");
        string audio = Write("ABC-12345.mp3");
        string graphics = Write("ABC-12345.cdg");
        string blocked = Write("ABC-12346.zip");
        string ready = Write("ABC-12347.zip");
        var request = new OperationRequest(OperationId.GroupByTrackId, [root])
        {
            OutputFolder = output, Grouping = new() { Move = move }, CheckTiming = timing
        };
        var plan = TrackGroupingPlanner.Plan(request.Inputs, output, request.Grouping);
        string Target(string source) => plan.Items.Single(item => item.Source == source).Destination;
        Directory.CreateDirectory(Path.GetDirectoryName(Target(blocked))!);
        File.WriteAllText(Target(blocked), "occupied");
        File.Delete(graphics);
        string added = Write("ABC-12348.zip");
        var recovery = new RecoveryStore(Path.Combine(root, "index"));

        var result = await new OperationExecutionService(recovery).ExecuteGroupingAsync(request, plan);

        Assert.Equal(3, result.Report.Entries.Count(entry => entry.Status == "Skipped"));
        Assert.Equal(ready, Assert.Single(result.Report.Entries, entry => entry.Status == "Succeeded").Source);
        Assert.True(File.Exists(added));
        Assert.True(File.Exists(audio));
        Assert.True(File.Exists(blocked));
        Assert.False(File.Exists(Target(audio)));
        Assert.Equal("occupied", File.ReadAllText(Target(blocked)));
        Assert.True(File.Exists(Target(ready)));
        Assert.Empty(recovery.Find());

        string Write(string name)
        {
            string path = Path.Combine(root, name);
            File.WriteAllText(path, name);
            return path;
        }
    }

    [Theory]
    [InlineData(FileCheckTiming.BeforeItem)]
    [InlineData(FileCheckTiming.BeforeBatch)]
    public async Task ApprovedGroupingRetainsRecoveryWhenAPairIsPartiallyMoved(FileCheckTiming timing)
    {
        string root = TestMediaFixture.NewFolder();
        string output = Path.Combine(root, "output");
        string graphics = Path.Combine(root, "ABC-12345.cdg");
        string audio = Path.Combine(root, "ABC-12345.mp3");
        File.WriteAllText(graphics, "graphics");
        File.WriteAllText(audio, "audio");
        var request = new OperationRequest(OperationId.GroupByTrackId, [root])
        {
            OutputFolder = output, Grouping = new() { Move = true }, CheckTiming = timing
        };
        var plan = TrackGroupingPlanner.Plan(request.Inputs, output, request.Grouping);
        string target = plan.Items.Single(item => item.Source == audio).Destination;
        var progress = new SynchronousProgress<ConversionProgress>(update =>
        {
            if (update.ActivitySource == audio)
                File.WriteAllText(target, "late collision");
        });
        var recovery = new RecoveryStore(Path.Combine(root, "index"));
        var service = new OperationExecutionService(recovery);

        await Assert.ThrowsAsync<IOException>(() => service.ExecuteGroupingAsync(request, plan, progress));

        Assert.False(File.Exists(graphics));
        Assert.Equal("audio", File.ReadAllText(audio));
        var job = Assert.Single(recovery.Find());
        Assert.Equal(0, WorkflowSession.Read(job.JournalPath).ActiveMoveGroup);
        File.Delete(target);
        var result = await service.ResumeAsync(job.JournalPath);
        Assert.False(result.HasIssues);
        Assert.Equal("audio", File.ReadAllText(target));
        Assert.Empty(recovery.Find());
    }

    [Theory]
    [InlineData(false, FileCheckTiming.BeforeBatch)]
    [InlineData(true, FileCheckTiming.BeforeBatch)]
    [InlineData(false, FileCheckTiming.BeforeItem)]
    [InlineData(true, FileCheckTiming.BeforeItem)]
    public async Task OnlyBatchTimingDetectsAnEditWhileAFileIsWaiting(bool move, FileCheckTiming timing)
    {
        string root = TestMediaFixture.NewFolder();
        string output = Path.Combine(root, "output");
        string first = Path.Combine(root, "ABC-12345.zip");
        string waiting = Path.Combine(root, "ABC-12346.zip");
        string destination = Path.Combine(output, "ABC-123xx", Path.GetFileName(waiting));
        File.WriteAllText(first, "first");
        File.WriteAllText(waiting, "before");
        var request = new OperationRequest(OperationId.GroupByTrackId, [first, waiting])
        {
            OutputFolder = output, Grouping = new() { Move = move }, CheckTiming = timing
        };
        var recovery = new RecoveryStore(Path.Combine(root, "index"));
        var service = new OperationExecutionService(recovery);
        var progress = new SynchronousProgress<ConversionProgress>(update =>
        {
            if (update.Completed != 1 || update.ActivityLabel != null || update.IsIndeterminate) return;
            string journal = Path.Combine(Assert.Single(Directory.GetDirectories(output, ".karaoke-job-workflow-*")), "workflow.json");
            var checkpoint = WorkflowSession.Read(journal);
            Assert.Equal(timing, checkpoint.CheckTiming);
            Assert.Equal(timing == FileCheckTiming.BeforeItem, checkpoint.Moves![1].Items[0].Hash.Length == 0);
            File.WriteAllText(waiting, "edited");
        });

        if (timing == FileCheckTiming.BeforeBatch)
        {
            await Assert.ThrowsAsync<IOException>(() => service.ExecuteAsync(request, progress));
            Assert.False(File.Exists(destination));
            Assert.Equal("edited", File.ReadAllText(waiting));
        }
        else
        {
            var result = await service.ExecuteAsync(request, progress);
            Assert.False(result.HasIssues);
            Assert.Equal("edited", File.ReadAllText(destination));
            Assert.Empty(recovery.Find());
        }
    }

    [Theory]
    [InlineData(RecoveryVerificationMode.MetadataOnly, true)]
    [InlineData(RecoveryVerificationMode.None, true)]
    [InlineData(RecoveryVerificationMode.Sha256, false)]
    public async Task InterruptedRunRequiringManualReviewCannotResume(RecoveryVerificationMode mode, bool saveResumableRun)
    {
        string root = TestMediaFixture.NewFolder();
        string output = Directory.CreateDirectory(Path.Combine(root, "output")).FullName;
        string source = Path.Combine(root, "ABC-12345.zip");
        string destination = Path.Combine(output, "track.zip");
        File.WriteAllText(source, "track");
        var store = new RecoveryStore(Path.Combine(root, "index"));
        string journal;
        using (var session = new WorkflowSession(store, new WorkflowData
        {
            Kind = RecoveryKind.TrackGrouping,
            OperationId = OperationId.GroupByTrackId,
            VerificationMode = mode,
            SaveResumableRun = saveResumableRun,
            Roots = [output, root],
            Moves = [new() { Items = [WorkflowMoves.Move(source, destination, mode)] }]
        }))
            journal = session.Journal;

        if (!saveResumableRun)
        {
            var legacy = JsonNode.Parse(File.ReadAllText(journal))!.AsObject();
            Assert.True(legacy.Remove("SupportsItemRecovery"));
            File.WriteAllText(journal, legacy.ToJsonString());
        }
        Assert.Contains("manual review", Assert.Single(store.Find()).Problem);
        await Assert.ThrowsAsync<IOException>(() => new OperationExecutionService(store).ResumeAsync(journal));
        Assert.True(File.Exists(journal));
        Assert.True(File.Exists(source));
        Assert.False(File.Exists(destination));
    }

    [Theory]
    [InlineData(FileCheckTiming.BeforeBatch)]
    [InlineData(FileCheckTiming.BeforeItem)]
    public async Task RecoveryRecreatesUnpublishedCopyStaging(FileCheckTiming timing)
    {
        string root = TestMediaFixture.NewFolder();
        string output = Directory.CreateDirectory(Path.Combine(root, "output")).FullName;
        string source = Path.Combine(root, "ABC-12345.zip");
        string target = Path.Combine(output, "ABC-123xx", Path.GetFileName(source));
        File.WriteAllText(source, "complete source");
        var store = new RecoveryStore(Path.Combine(root, "index"));
        string journal;
        using (var session = new WorkflowSession(store, new WorkflowData
        {
            Kind = RecoveryKind.TrackGrouping, OperationId = OperationId.GroupByTrackId, Roots = [output, root],
            CopyGrouping = true, SaveResumableRun = false, CheckTiming = timing,
            Moves = [new() { Items = [WorkflowMoves.Move(source, target, timing: timing)] }]
        }))
        {
            journal = session.Journal;
            session.BeginMoveGroup(0);
            string staging = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(journal)!, "copy-0")).FullName;
            File.WriteAllText(Path.Combine(staging, "0"), "partial");
        }

        var result = await new OperationExecutionService(store).ResumeAsync(journal);
        Assert.False(result.HasIssues);
        Assert.Equal("complete source", File.ReadAllText(target));
        Assert.Equal("complete source", File.ReadAllText(source));
        Assert.Empty(store.Find());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MoveCanGroupAnExplicitFileWithinTheDestinationRoot(bool continueBatch)
    {
        string root = TestMediaFixture.NewFolder();
        string output = Directory.CreateDirectory(Path.Combine(root, "output")).FullName;
        string source = Path.Combine(output, "BKD-33601.zip");
        File.WriteAllText(source, "track");
        var request = new OperationRequest(OperationId.GroupByTrackId, [source])
        {
            OutputFolder = output,
            SaveResumableRun = continueBatch,
            Grouping = new() { Move = true }
        };
        var service = new OperationExecutionService(new RecoveryStore(Path.Combine(root, "index")));
        var plan = TrackGroupingPlanner.Plan(request.Inputs, output, request.Grouping);
        var events = new List<ConversionProgress>();

        var progress = new SynchronousProgress<ConversionProgress>(update =>
        {
            events.Add(update);
            if (update.ActivityLabel == "Processing")
            {
                string workspace = Assert.Single(Directory.GetDirectories(output, ".karaoke-job-workflow-*"));
                var checkpoint = WorkflowSession.Read(Path.Combine(workspace, "workflow.json"));
                Assert.Equal(RecoveryVerificationMode.XxHash128, checkpoint.VerificationMode);
                Assert.Equal(FileCheckTiming.BeforeItem, checkpoint.CheckTiming);
                Assert.Equal(0, checkpoint.ActiveMoveGroup);
            }
        });
        var result = await service.ExecuteGroupingAsync(request, plan, progress);

        Assert.False(result.HasIssues);
        Assert.StartsWith("Preparing recovery", events[0].Message);
        Assert.True(events[0].IsIndeterminate);
        Assert.DoesNotContain(events.Skip(1), progress => progress.IsIndeterminate);
        Assert.Equal(100, events[^1].TotalPercent);
        Assert.False(File.Exists(source));
        Assert.Equal("track", File.ReadAllText(Path.Combine(output, "BKD-336xx", "BKD-33601.zip")));
    }

    [Theory]
    [InlineData(false, RecoveryVerificationMode.Sha256, FileCheckTiming.BeforeBatch)]
    [InlineData(false, RecoveryVerificationMode.XxHash128, FileCheckTiming.BeforeBatch)]
    [InlineData(false, RecoveryVerificationMode.MetadataOnly, FileCheckTiming.BeforeBatch)]
    [InlineData(false, RecoveryVerificationMode.None, FileCheckTiming.BeforeBatch)]
    [InlineData(true, RecoveryVerificationMode.Sha256, FileCheckTiming.BeforeBatch)]
    [InlineData(true, RecoveryVerificationMode.XxHash128, FileCheckTiming.BeforeBatch)]
    [InlineData(true, RecoveryVerificationMode.MetadataOnly, FileCheckTiming.BeforeBatch)]
    [InlineData(true, RecoveryVerificationMode.None, FileCheckTiming.BeforeBatch)]
    [InlineData(false, RecoveryVerificationMode.Sha256, FileCheckTiming.BeforeItem)]
    [InlineData(false, RecoveryVerificationMode.XxHash128, FileCheckTiming.BeforeItem)]
    [InlineData(false, RecoveryVerificationMode.MetadataOnly, FileCheckTiming.BeforeItem)]
    [InlineData(false, RecoveryVerificationMode.None, FileCheckTiming.BeforeItem)]
    [InlineData(true, RecoveryVerificationMode.Sha256, FileCheckTiming.BeforeItem)]
    [InlineData(true, RecoveryVerificationMode.XxHash128, FileCheckTiming.BeforeItem)]
    [InlineData(true, RecoveryVerificationMode.MetadataOnly, FileCheckTiming.BeforeItem)]
    [InlineData(true, RecoveryVerificationMode.None, FileCheckTiming.BeforeItem)]
    public async Task WholeFolderPublicationPreservesContentsAndReportsSuccess(bool move, RecoveryVerificationMode verificationMode, FileCheckTiming timing)
    {
        string root = TestMediaFixture.NewFolder();
        string source = Directory.CreateDirectory(Path.Combine(root, "BKD-31xxx")).FullName;
        string nested = Directory.CreateDirectory(Path.Combine(source, "extras")).FullName;
        File.WriteAllText(Path.Combine(nested, "notes.txt"), "contents");
        string output = Path.Combine(root, "output");
        var store = new RecoveryStore(Path.Combine(root, "index"));
        var request = new OperationRequest(OperationId.GroupByTrackId, [source])
        {
            OutputFolder = output,
            RecoveryVerification = verificationMode,
            CheckTiming = timing,
            Grouping = new() { WholeFolders = true, TrailingDigits = 4, Move = move }
        };

        var report = await new OperationExecutionService(store).ExecuteAsync(request);

        Assert.False(report.HasIssues);
        Assert.Equal("contents", File.ReadAllText(Path.Combine(output, "BKD-3xxxx", "BKD-31xxx", "extras", "notes.txt")));
        Assert.Equal(!move, Directory.Exists(source));
        Assert.Empty(store.Find());
    }

    [Theory]
    [InlineData(false, false, FileCheckTiming.BeforeBatch)]
    [InlineData(false, false, FileCheckTiming.BeforeItem)]
    [InlineData(false, true, FileCheckTiming.BeforeBatch)]
    [InlineData(false, true, FileCheckTiming.BeforeItem)]
    [InlineData(true, false, FileCheckTiming.BeforeBatch)]
    [InlineData(true, false, FileCheckTiming.BeforeItem)]
    [InlineData(true, true, FileCheckTiming.BeforeBatch)]
    [InlineData(true, true, FileCheckTiming.BeforeItem)]
    public async Task RecoveryFinishesAPartiallyPublishedPair(bool move, bool continueBatch, FileCheckTiming timing)
    {
        string root = TestMediaFixture.NewFolder();
        string output = Directory.CreateDirectory(Path.Combine(root, "output")).FullName;
        string destination = Directory.CreateDirectory(Path.Combine(output, "ABC-123xx")).FullName;
        string audio = Path.Combine(root, "ABC-12345.mp3"), graphics = Path.ChangeExtension(audio, ".cdg");
        File.WriteAllText(audio, "audio");
        File.WriteAllText(graphics, "graphics");
        string audioTarget = Path.Combine(destination, Path.GetFileName(audio));
        string graphicsTarget = Path.Combine(destination, Path.GetFileName(graphics));
        string waiting = Path.Combine(root, "ABC-12346.zip");
        string waitingTarget = Path.Combine(destination, Path.GetFileName(waiting));
        File.WriteAllText(waiting, "waiting");
        var store = new RecoveryStore(Path.Combine(root, "index"));
        string journal;
        using (var session = new WorkflowSession(store, new WorkflowData
        {
            Kind = RecoveryKind.TrackGrouping,
            OperationId = OperationId.GroupByTrackId,
            Roots = [output, root],
            SaveResumableRun = continueBatch,
            CheckTiming = timing,
            CopyGrouping = !move,
            Moves = [
                new() { Items = [WorkflowMoves.Move(audio, audioTarget), WorkflowMoves.Move(graphics, graphicsTarget)] },
                new() { Items = [WorkflowMoves.Move(waiting, waitingTarget, timing: timing)] }
            ]
        }))
        {
            journal = session.Journal;
            session.BeginMoveGroup(0);
            if (move) File.Move(audio, audioTarget);
            else
            {
                File.Copy(audio, audioTarget);
                string staging = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(journal)!, "copy-0")).FullName;
                File.Copy(graphics, Path.Combine(staging, "1"));
                session.Data.Moves![0].CopyPrepared = true;
                session.Save();
            }
        }

        if (move && continueBatch && timing == FileCheckTiming.BeforeBatch)
        {
            var legacy = JsonNode.Parse(File.ReadAllText(journal))!.AsObject();
            Assert.True(legacy.Remove("VerificationMode"));
            Assert.True(legacy.Remove("CheckTiming"));
            File.WriteAllText(journal, legacy.ToJsonString());
            Assert.Equal(FileCheckTiming.BeforeBatch, WorkflowSession.Read(journal).CheckTiming);
        }
        var job = Assert.Single(store.Find());
        Assert.Equal(RecoveryKind.TrackGrouping, job.Kind);
        Assert.Equal(continueBatch, job.ContinueBatchAfterRecovery);
        var result = await new OperationExecutionService(store).ResumeAsync(journal);

        Assert.False(result.HasIssues);
        Assert.Equal("audio", File.ReadAllText(audioTarget));
        Assert.Equal("graphics", File.ReadAllText(graphicsTarget));
        Assert.Equal(!move, File.Exists(graphics));
        Assert.Equal(continueBatch, File.Exists(waitingTarget));
        Assert.Equal(!move || !continueBatch, File.Exists(waiting));
        Assert.Empty(store.Find());
    }
}
