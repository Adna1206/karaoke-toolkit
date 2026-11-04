using System.Diagnostics;
using KaraokeToolkit.App.Features.Main;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Recipes;
using KaraokeToolkit.Core.Recovery;
using KaraokeToolkit.Core.Tagging;
using Action = KaraokeToolkit.Core.Conversion.OperationCatalog.defAction;

namespace KaraokeToolkit.Tests.Recovery;

public class WorkflowRecoveryTests
{
    [Fact]
    public async Task CheckpointReplacementSurvivesBriefWindowsReaderLock()
    {
        if (!OperatingSystem.IsWindows()) return;
        string path = Path.Combine(TestMediaFixture.NewFolder(), "checkpoint.json");
        DurableJson.Write(path, "original");
        var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var release = Task.Run(() => { Thread.Sleep(60); reader.Dispose(); });
        try { DurableJson.Write(path, "updated"); }
        finally { await release; reader.Dispose(); }
        Assert.Equal("\"updated\"", File.ReadAllText(path)); Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void PermanentlyBlockedCheckpointPreservesOriginalAndPendingWrite()
    {
        if (!OperatingSystem.IsWindows()) return;
        string path = Path.Combine(TestMediaFixture.NewFolder(), "checkpoint.json");
        DurableJson.Write(path, "original");
        using var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var error = Record.Exception(() => DurableJson.Write(path, "updated"));
        Assert.True(error is IOException or UnauthorizedAccessException);
        Assert.Equal("\"original\"", File.ReadAllText(path));
        Assert.Equal("\"updated\"", File.ReadAllText(path + ".tmp"));
    }

    [Fact]
    public void LaterRootFindsRecipeThroughChildWhenParentIndexIsMissing()
    {
        string first = TestMediaFixture.NewFolder(), second = TestMediaFixture.NewFolder(), index = TestMediaFixture.NewFolder();
        string source = Path.Combine(second, "song.zip"); File.WriteAllText(source, "unchanged");
        var store = new RecoveryStore(index);
        var recipe = new RecipeDefinition { Name = "Across roots", Steps = [new() { OperationId = "Zip_To_AudioG" }] };
        string parent;
        using (var session = new WorkflowSession(store, new WorkflowData
        { Kind = RecoveryKind.Recipe, Roots = [first, second], Inputs = [source], CurrentInputs = [source], Recipe = recipe }))
        {
            session.Enter(); session.StartStep();
            session.Data.Conversions = [new() { Folder = second, Sources = [source] }]; session.Save();
            using var child = new RunSession(store, new OperationRequest(Action.Zip_To_AudioG, second, new()), OverwritePolicy.Skip, [source]);
            parent = session.Journal;
        }
        File.Delete(store.IndexPath(parent));
        var job = Assert.Single(store.Find(second));
        Assert.Equal(RecoveryKind.Recipe, job.Kind); Assert.Equal(parent, job.JournalPath);
    }

    [Fact]
    public async Task ExplicitCancellationBeforeChildPreparationDoesNotLeaveRecovery()
    {
        string folder = TestMediaFixture.NewFolder(), index = TestMediaFixture.NewFolder();
        string source = Path.Combine(folder, "song.zip"); File.WriteAllText(source, "unchanged");
        var store = new RecoveryStore(index);
        var recipe = new RecipeDefinition { Name = "Cancelled", Steps = [new() { OperationId = "Zip_To_AudioG" }] };
        using (var session = new WorkflowSession(store, new WorkflowData
        { Kind = RecoveryKind.Recipe, Roots = [folder], Inputs = [source], CurrentInputs = [source], Recipe = recipe }))
        {
            session.Enter(); session.StartStep();
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new SelectionConversionService(store).RunAsync(
                new(Action.Zip_To_AudioG, [source], new(), OverwritePolicy.Skip), token: cancellation.Token));
            session.Finish();
        }
        Assert.Empty(store.Find()); Assert.Empty(Directory.GetDirectories(folder, ".karaoke-job-*"));
        Assert.Equal("unchanged", File.ReadAllText(source));
    }

    private static async Task Crash(string mode, string folder, string index)
    {
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true };
        foreach (string argument in new[] { typeof(WorkflowRecoveryTests).Assembly.Location, "--workflow-crash-worker", mode, folder, index }) start.ArgumentList.Add(argument);
        using var child = Process.Start(start)!;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            while (!File.Exists(Path.Combine(folder, "workflow-crash-ready")))
            { Assert.False(child.HasExited, "Workflow worker exited before its crash boundary."); await Task.Delay(50, timeout.Token); }
            Assert.Empty(new RecoveryStore(index).Find()); // Owned workflows and their children are hidden while running.
            child.Kill(true); await child.WaitForExitAsync(timeout.Token);
        }
        finally { if (!child.HasExited) { child.Kill(true); await child.WaitForExitAsync(); } }
    }

    [Theory]
    [InlineData("between", 1)]
    [InlineData("conversion", 0)]
    public async Task KilledRecipeResumesSavedPipelineAndOnlyOffersParent(string mode, int completed)
    {
        string folder = TestMediaFixture.NewFolder(), index = TestMediaFixture.NewFolder();
        TestMediaFixture.Seed(folder, Action.Zip_To_Bin, "keep");
        await Crash(mode, folder, index);
        var store = new RecoveryStore(index);
        var job = Assert.Single(store.Find());
        Assert.Equal(RecoveryKind.Recipe, job.Kind); Assert.Equal(completed, job.Completed); Assert.Equal(2, job.Total);
        Assert.Equal(job.JournalPath, Assert.Single(store.Find(folder)).JournalPath);
        if (mode == "between") Assert.False(File.Exists(Path.Combine(folder, "song.zip"))); // Original was deliberately deleted by step 1.
        var runner = new RecipeRunner(new RecipeStepExecutor(store), store);
        var result = await runner.ResumeAsync(job);
        Assert.Empty(result.StopReason); Assert.Equal(2, result.Steps.Count);
        Assert.All(result.Steps, step => Assert.Equal(1, step.Result.Succeeded));
        Assert.True(File.Exists(Path.Combine(folder, "song.zip")));
        Assert.False(File.Exists(Path.Combine(folder, "song.wav"))); Assert.False(File.Exists(Path.Combine(folder, "song.cdg")));
        Assert.Empty(store.Find()); Assert.Empty(Directory.GetDirectories(folder, ".karaoke-job-*"));
    }

    [Fact]
    public async Task StartupTagRecoveryUsesSavedPreviewSkipsFinishedFileAndSupportsLater()
    {
        string folder = TestMediaFixture.NewFolder(), settings = TestMediaFixture.NewFolder(), index = Path.Combine(settings, "Recovery");
        TestMediaFixture.Seed(folder, Action.AudioG_To_Bin, "keep");
        string first = Path.Combine(folder, "DISC-00 - Artist - First.wav"), second = Path.Combine(folder, "DISC-01 - Artist - Second.wav");
        File.Move(Path.Combine(folder, "song.wav"), first); File.Copy(first, second);
        await Crash("tagging", folder, index);
        var store = new RecoveryStore(index); var job = Assert.Single(store.Find());
        Assert.Equal(RecoveryKind.Tagging, job.Kind); Assert.Equal(1, job.Completed);
        string firstHash = FileTransaction.Hash(first);
        var model = new MainViewModel(Path.Combine(settings, "settings.xml"));
        model.ConfirmRecoveryAsync = _ => Task.FromResult(false);
        Assert.True(await model.CheckForRecoveryAsync()); Assert.False(model.ShowProgress);
        Assert.Single(store.Find()); Assert.Equal(firstHash, FileTransaction.Hash(first));
        model.ConfigureTaggingAsync = _ => throw new InvalidOperationException("Recovery must not reopen the tagging configuration dialog.");
        model.ConfirmRecoveryAsync = pending => { Assert.Equal(RecoveryKind.Tagging, pending.Kind); return Task.FromResult(true); };
        Assert.True(await model.CheckForRecoveryAsync());
        Assert.Equal("Tagging Report", model.LastReport!.ReportType); Assert.Contains("2 updated", model.Status);
        Assert.True(model.ShowProgress); Assert.False(model.IsRecoveryMode); Assert.False(model.RecoveryAvailable);
        Assert.Equal(firstHash, FileTransaction.Hash(first));
        Assert.Equal("Saved preview title", KaraokeTaggingService.ReadTags(second).Title);
        Assert.Empty(store.Find());
    }

    [Fact]
    public async Task StartupRecipeRecoveryDoesNotRequireRecipeLibraryEntry()
    {
        string folder = TestMediaFixture.NewFolder(), settings = TestMediaFixture.NewFolder(), index = Path.Combine(settings, "Recovery");
        TestMediaFixture.Seed(folder, Action.Zip_To_Bin, "keep"); await Crash("between", folder, index);
        var model = new MainViewModel(Path.Combine(settings, "settings.xml"));
        Assert.Empty(model.RecipeChoices);
        model.ConfirmRecoveryAsync = job => { Assert.Equal(RecoveryKind.Recipe, job.Kind); return Task.FromResult(true); };
        Assert.True(await model.CheckForRecoveryAsync());
        Assert.Equal("Recipe Report", model.LastReport!.ReportType); Assert.Contains("2 steps completed", model.Status);
        Assert.False(model.RecoveryAvailable); Assert.False(model.IsBusy); Assert.True(File.Exists(Path.Combine(folder, "song.zip")));
    }

    [Fact]
    public async Task RecipeTaggingCrashOffersOnlyParentSkipsFinishedTagsAndContinuesToZip()
    {
        string folder = TestMediaFixture.NewFolder(), index = TestMediaFixture.NewFolder();
        TestMediaFixture.Seed(folder, Action.AudioG_To_Bin, "keep");
        string first = Path.Combine(folder, "DISC-00 - Artist - First.wav"), second = Path.Combine(folder, "DISC-01 - Artist - Second.wav");
        File.Move(Path.Combine(folder, "song.wav"), first); File.Copy(first, second);
        File.Move(Path.Combine(folder, "song.cdg"), Path.ChangeExtension(first, ".cdg")); File.Copy(Path.ChangeExtension(first, ".cdg"), Path.ChangeExtension(second, ".cdg"));
        await Crash("recipe-tagging", folder, index);
        var store = new RecoveryStore(index); var job = Assert.Single(store.Find());
        Assert.Equal(RecoveryKind.Recipe, job.Kind); Assert.Equal(0, job.Completed);
        string hash = FileTransaction.Hash(first);
        var result = await new RecipeRunner(recovery: store).ResumeAsync(job);
        Assert.Empty(result.StopReason); Assert.Equal(2, result.Steps.Count);
        Assert.Equal(2, result.Steps[0].Result.Succeeded); Assert.Equal(hash, FileTransaction.Hash(first));
        Assert.Equal("Second", KaraokeTaggingService.ReadTags(second).Title);
        Assert.True(File.Exists(Path.ChangeExtension(first, ".zip"))); Assert.True(File.Exists(Path.ChangeExtension(second, ".zip")));
        Assert.Empty(store.Find()); Assert.Empty(Directory.GetDirectories(folder, ".karaoke-job-*"));
    }

    [Fact]
    public async Task ChangedCompletedOutputBlocksRecoveryAndKeepsJournal()
    {
        string folder = TestMediaFixture.NewFolder(), index = TestMediaFixture.NewFolder();
        TestMediaFixture.Seed(folder, Action.Zip_To_Bin, "keep"); await Crash("between", folder, index);
        string audio = Path.Combine(folder, "song.wav"); File.AppendAllText(audio, "user edit");
        string hash = FileTransaction.Hash(audio); var store = new RecoveryStore(index);
        await Assert.ThrowsAsync<IOException>(() => new RecipeRunner(new RecipeStepExecutor(store), store).ResumeAsync(Assert.Single(store.Find())));
        Assert.Equal(hash, FileTransaction.Hash(audio)); Assert.Single(store.Find()); Assert.False(File.Exists(Path.Combine(folder, "song.zip")));
    }

    [Fact]
    public async Task FlattenCrashUsesOriginalPlanInsteadOfFlatteningAnotherLayer()
    {
        string root = TestMediaFixture.NewFolder(), index = TestMediaFixture.NewFolder();
        string child = Directory.CreateDirectory(Path.Combine(root, "child")).FullName;
        string nested = Directory.CreateDirectory(Path.Combine(child, "nested")).FullName;
        File.WriteAllText(Path.Combine(nested, "track.txt"), "unchanged");
        await Crash("moving", root, index);
        var store = new RecoveryStore(index);
        var result = await new RecipeRunner(new RecipeStepExecutor(store), store).ResumeAsync(Assert.Single(store.Find()));
        Assert.True(File.Exists(Path.Combine(root, "nested", "track.txt")));
        Assert.False(File.Exists(Path.Combine(root, "track.txt"))); Assert.False(Directory.Exists(child));
        Assert.Equal(2, result.Steps.Count); Assert.Empty(store.Find());
    }

    [Fact]
    public async Task HalfMovedPairIsFinishedAndPassedToRemainingStep()
    {
        string folder = TestMediaFixture.NewFolder(), index = TestMediaFixture.NewFolder();
        string source = Path.Combine(folder, "track.zip"), target = Path.Combine(folder, "T", "track.zip");
        File.WriteAllText(source, "payload");
        var recipe = new RecipeDefinition { Name = "Move safely", Steps = [new() { OperationId = "OrganizeFiles" }, new() { OperationId = "OrganizeFiles" }] };
        string journal;
        var store = new RecoveryStore(index);
        using (var session = new WorkflowSession(store, new WorkflowData { Kind = RecoveryKind.Recipe, Roots = [folder], Inputs = [source], CurrentInputs = [source], Recipe = recipe }))
        {
            session.StartStep(); session.Data.Moves = [new() { Items = [WorkflowMoves.Move(source, target)] }]; session.Save(); journal = session.Journal;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Move(source, target); // Crash before the group receipt.
        }
        var result = await new RecipeRunner(new RecipeStepExecutor(store), store).ResumeAsync(Assert.Single(store.Find()));
        Assert.Equal(2, result.Steps.Count); Assert.Equal("payload", File.ReadAllText(target));
        Assert.False(File.Exists(source)); Assert.Empty(store.Find()); Assert.False(File.Exists(journal));
    }

    [Fact]
    public async Task CollisionInPartiallyMovedPairRetainsCheckpointAndBothFiles()
    {
        string folder = TestMediaFixture.NewFolder(), index = TestMediaFixture.NewFolder();
        string audio = Path.Combine(folder, "track.mp3"), graphics = Path.Combine(folder, "track.cdg");
        string bucket = Directory.CreateDirectory(Path.Combine(folder, "T")).FullName;
        string movedAudio = Path.Combine(bucket, "track.mp3"), blockedGraphics = Path.Combine(bucket, "track.cdg");
        File.WriteAllText(audio, "audio"); File.WriteAllText(graphics, "graphics");
        var store = new RecoveryStore(index);
        var recipe = new RecipeDefinition { Name = "Pair collision", Steps = [new() { OperationId = "OrganizeFiles" }] };
        using (var session = new WorkflowSession(store, new WorkflowData
        { Kind = RecoveryKind.Recipe, Roots = [folder], Inputs = [audio, graphics], CurrentInputs = [audio, graphics], Recipe = recipe }))
        {
            session.StartStep(); session.Data.Moves = [new() { Items = [WorkflowMoves.Move(audio, movedAudio), WorkflowMoves.Move(graphics, blockedGraphics)] }]; session.Save();
            File.Move(audio, movedAudio); File.WriteAllText(blockedGraphics, "unrelated");
        }
        var job = Assert.Single(store.Find());
        await Assert.ThrowsAsync<IOException>(() => new RecipeRunner(recovery: store).ResumeAsync(job));
        Assert.True(File.Exists(job.JournalPath)); Assert.Single(store.Find());
        Assert.Equal("audio", File.ReadAllText(movedAudio)); Assert.Equal("graphics", File.ReadAllText(graphics));
        Assert.Equal("unrelated", File.ReadAllText(blockedGraphics));
        File.Delete(blockedGraphics); // Resolving the collision makes the same checkpoint resumable.
        var result = await new RecipeRunner(recovery: store).ResumeAsync(job);
        Assert.Empty(result.StopReason); Assert.Equal("graphics", File.ReadAllText(blockedGraphics)); Assert.Empty(store.Find());
    }
}
