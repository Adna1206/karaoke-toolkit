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

        Assert.Contains("manual review", Assert.Single(store.Find()).Problem);
        await Assert.ThrowsAsync<IOException>(() => new OperationExecutionService(store).ResumeAsync(journal));
        Assert.True(File.Exists(journal));
        Assert.True(File.Exists(source));
        Assert.False(File.Exists(destination));
    }

    [Fact]
    public async Task MoveCanGroupAnExplicitFileWithinTheDestinationRoot()
    {
        string root = TestMediaFixture.NewFolder();
        string output = Directory.CreateDirectory(Path.Combine(root, "output")).FullName;
        string source = Path.Combine(output, "BKD-33601.zip");
        File.WriteAllText(source, "track");
        var request = new OperationRequest(OperationId.GroupByTrackId, [source])
        {
            OutputFolder = output,
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
                Assert.Equal(RecoveryVerificationMode.XxHash128, WorkflowSession.Read(Path.Combine(workspace, "workflow.json")).VerificationMode);
            }
        });
        var result = await service.ExecuteGroupingAsync(request, plan, progress);

        Assert.False(result.HasIssues);
        Assert.Collection(events.Take(2),
            progress =>
            {
                Assert.StartsWith("Checking grouping plan", progress.Message);
                Assert.True(progress.IsIndeterminate);
            },
            progress =>
            {
                Assert.StartsWith("Preparing recovery", progress.Message);
                Assert.True(progress.IsIndeterminate);
            });
        Assert.DoesNotContain(events.Skip(2), progress => progress.IsIndeterminate);
        Assert.Equal(100, events[^1].TotalPercent);
        Assert.False(File.Exists(source));
        Assert.Equal("track", File.ReadAllText(Path.Combine(output, "BKD-336xx", "BKD-33601.zip")));
    }

    [Theory]
    [InlineData(false, RecoveryVerificationMode.Sha256)]
    [InlineData(false, RecoveryVerificationMode.XxHash128)]
    [InlineData(false, RecoveryVerificationMode.MetadataOnly)]
    [InlineData(false, RecoveryVerificationMode.None)]
    [InlineData(true, RecoveryVerificationMode.Sha256)]
    [InlineData(true, RecoveryVerificationMode.XxHash128)]
    [InlineData(true, RecoveryVerificationMode.MetadataOnly)]
    [InlineData(true, RecoveryVerificationMode.None)]
    public async Task WholeFolderPublicationPreservesContentsAndReportsSuccess(bool move, RecoveryVerificationMode verificationMode)
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
            Grouping = new() { WholeFolders = true, TrailingDigits = 4, Move = move }
        };

        var report = await new OperationExecutionService(store).ExecuteAsync(request);

        Assert.False(report.HasIssues);
        Assert.Equal("contents", File.ReadAllText(Path.Combine(output, "BKD-3xxxx", "BKD-31xxx", "extras", "notes.txt")));
        Assert.Equal(!move, Directory.Exists(source));
        Assert.Empty(store.Find());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecoveryFinishesAPartiallyPublishedPair(bool move)
    {
        string root = TestMediaFixture.NewFolder();
        string output = Directory.CreateDirectory(Path.Combine(root, "output")).FullName;
        string destination = Directory.CreateDirectory(Path.Combine(output, "ABC-123xx")).FullName;
        string audio = Path.Combine(root, "ABC-12345.mp3"), graphics = Path.ChangeExtension(audio, ".cdg");
        File.WriteAllText(audio, "audio");
        File.WriteAllText(graphics, "graphics");
        string audioTarget = Path.Combine(destination, Path.GetFileName(audio));
        string graphicsTarget = Path.Combine(destination, Path.GetFileName(graphics));
        var store = new RecoveryStore(Path.Combine(root, "index"));
        string journal;
        using (var session = new WorkflowSession(store, new WorkflowData
        {
            Kind = RecoveryKind.TrackGrouping,
            OperationId = OperationId.GroupByTrackId,
            Roots = [output, root],
            CopyGrouping = !move,
            Moves = [new() { CopyPrepared = !move, Items = [WorkflowMoves.Move(audio, audioTarget), WorkflowMoves.Move(graphics, graphicsTarget)] }]
        }))
        {
            journal = session.Journal;
            if (move) File.Move(audio, audioTarget);
            else
            {
                File.Copy(audio, audioTarget);
                string staging = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(journal)!, "copy-0")).FullName;
                File.Copy(graphics, Path.Combine(staging, "1"));
            }
        }

        if (move)
        {
            var legacy = JsonNode.Parse(File.ReadAllText(journal))!.AsObject();
            Assert.True(legacy.Remove("VerificationMode"));
            File.WriteAllText(journal, legacy.ToJsonString());
        }
        Assert.Equal(RecoveryKind.TrackGrouping, Assert.Single(store.Find()).Kind);
        var result = await new OperationExecutionService(store).ResumeAsync(journal);

        Assert.False(result.HasIssues);
        Assert.Equal("audio", File.ReadAllText(audioTarget));
        Assert.Equal("graphics", File.ReadAllText(graphicsTarget));
        Assert.Equal(!move, File.Exists(graphics));
        Assert.Empty(store.Find());
    }
}
