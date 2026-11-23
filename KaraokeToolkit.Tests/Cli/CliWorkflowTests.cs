using System.Text.Json;
using KaraokeToolkit.Cli;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Execution;
using KaraokeToolkit.Core.Recipes;
using KaraokeToolkit.Core.Recovery;
using KaraokeToolkit.Tests.DiscRipping;

namespace KaraokeToolkit.Tests.Cli;

public class CliWorkflowTests
{
    private static CliApplication App(RecoveryStore? store = null) => new(new OperationExecutionService(store ?? new(TestMediaFixture.NewFolder())));
    private static Dictionary<string, string> Snapshot(string root) => Directory.GetFiles(root, "*", SearchOption.AllDirectories)
        .ToDictionary(p => Path.GetRelativePath(root, p), FileTransaction.Hash);

    [Fact]
    public async Task TrackGroupingPreviewsAndCopiesWholeBatches()
    {
        string source = TestMediaFixture.NewFolder(), destination = TestMediaFixture.NewFolder();
        string batch = Directory.CreateDirectory(Path.Combine(source, "BKD-31xxx")).FullName;
        File.WriteAllText(Path.Combine(batch, "BKD-31001.zip"), "original");
        string[] args = ["run", "group-by-track-id", batch, "--output", destination, "--whole-folders", "--trailing-digits", "4", "--mask", "X"];
        var cli = App();
        var preview = new StringWriter();
        Assert.Equal(0, await cli.RunAsync([.. args, "--dry-run"], preview, new StringWriter()));
        Assert.Contains("BKD-3XXXX", preview.ToString());
        Assert.Empty(Directory.EnumerateFileSystemEntries(destination));

        Assert.Equal(0, await cli.RunAsync(args, new StringWriter(), new StringWriter()));
        Assert.Equal("original", File.ReadAllText(Path.Combine(destination, "BKD-3XXXX", "BKD-31xxx", "BKD-31001.zip")));
        Assert.True(Directory.Exists(batch));
        Assert.Equal(1, await cli.RunAsync(args, new StringWriter(), new StringWriter()));
    }

    [Fact]
    public void TrackGroupingAcceptsLiteralSeparatorsAndRejectsConflictingModes()
    {
        string[] args = ["run", "group-by-track-id", "BKD33601.zip", "--output", ".", "--range-size", "50", "--id-separator=", "--range-separator", " to "];
        var options = CliArguments.Parse(args).Request!.Grouping;
        Assert.Equal(50, options.RangeSize);
        Assert.Equal("", options.IdSeparator);
        Assert.Equal(" to ", options.RangeSeparator);
        Assert.Throws<CliUsageException>(() => CliArguments.Parse([.. args, "--create-parents"]));
        Assert.Throws<CliUsageException>(() => CliArguments.Parse([.. args, "--overwrite", "replace"]));
    }

    [Theory]
    [InlineData("zip-to-audiog")]
    [InlineData("zip-to-bin")]
    public async Task OutputFolderAndDryRunPreserveInputs(string operation)
    {
        string source = TestMediaFixture.NewFolder(), destination = TestMediaFixture.NewFolder();
        TestMediaFixture.Seed(source, OperationId.ZipToBin, "keep"); var original = Snapshot(source);
        var cli = App();
        string[] args = ["run", operation, source, "--output", destination + Path.DirectorySeparatorChar, "--no-cue-text", "--json", "--quiet"];
        var output = new StringWriter();
        Assert.Equal(0, await cli.RunAsync([.. args, "--dry-run"], output, new StringWriter()));
        Assert.Empty(Directory.GetFileSystemEntries(destination)); Assert.Equal(original, Snapshot(source));
        using var planned = JsonDocument.Parse(output.ToString());
        Assert.Equal("Dry Run Report", planned.RootElement.GetProperty("Report").GetProperty("ReportType").GetString());
        Assert.Equal(0, await cli.RunAsync(args, new StringWriter(), new StringWriter()));
        Assert.Equal(original, Snapshot(source)); Assert.NotEmpty(Directory.GetFiles(destination));
        Assert.DoesNotContain(Directory.GetDirectories(destination), p => Path.GetFileName(p).StartsWith(".karaoke-job-"));
    }

    [Fact]
    public async Task ExternalOutputRecoveryDiscardsInterruptedStageAndUsesSavedDestination()
    {
        string source = TestMediaFixture.NewFolder(), destination = TestMediaFixture.NewFolder();
        TestMediaFixture.Seed(source, OperationId.ZipToBin, "keep");
        var store = new RecoveryStore(TestMediaFixture.NewFolder()); string journal;
        using (var run = new RunSession(store, new(OperationId.ZipToAudioG, source, new() { DeleteOldFiles = false, AutoCreateCueText = true })
            { OutputFolder = destination }, OverwritePolicy.Fail, [Path.Combine(source, "song.zip")]))
        {
            run.Begin(Path.Combine(source, "song.zip"));
            string workspace = Directory.CreateDirectory(Path.Combine(destination, ".karaoke-job-" + Guid.NewGuid().ToString("N"))).FullName;
            run.Attach(workspace); File.WriteAllText(Path.Combine(workspace, "partial"), "unfinished"); journal = run.Journal;
        }
        Assert.Equal(0, await App(store).RunAsync(["recover", journal, "--quiet"], new StringWriter(), new StringWriter()));
        Assert.True(File.Exists(Path.Combine(destination, "song.wav")));
        Assert.Single(Directory.GetFiles(destination, "*.txt")); Assert.True(File.Exists(Path.Combine(source, "song.zip")));
        Assert.Empty(store.Find());
    }

    [Fact]
    public async Task ExternalLosslessEncodingPublishesGraphicsAlongsideAudio()
    {
        string source = TestMediaFixture.NewFolder(), destination = TestMediaFixture.NewFolder();
        TestMediaFixture.Seed(source, OperationId.AudioGToBin, "keep");
        var original = Snapshot(source);
        Assert.Equal(0, await App().RunAsync(["run", "lossless-audiog-to-mp3g", source, "--output", destination, "--no-cue-text", "--quiet"], new StringWriter(), new StringWriter()));
        Assert.True(File.Exists(Path.Combine(destination, "song.mp3")));
        Assert.Equal(FileTransaction.Hash(Path.Combine(source, "song.cdg")), FileTransaction.Hash(Path.Combine(destination, "song.cdg")));
        Assert.Equal(original, Snapshot(source));
    }

    [Fact]
    public void RecoveryRejectsDestinationWorkspaceOutsideTheSavedOutputFolder()
    {
        string root = TestMediaFixture.NewFolder(), output = TestMediaFixture.NewFolder(), other = TestMediaFixture.NewFolder();
        string source = Path.Combine(root, "song.zip"); File.WriteAllText(source, "source");
        var store = new RecoveryStore(TestMediaFixture.NewFolder()); string journal;
        using (var run = new RunSession(store, new(OperationId.ZipToAudioG, root, new() { DeleteOldFiles = false }) { OutputFolder = output }, OverwritePolicy.Fail, [source]))
        { run.Begin(source); run.Attach(Path.Combine(other, ".karaoke-job-" + Guid.NewGuid().ToString("N"))); journal = run.Journal; }
        Assert.Throws<IOException>(() => RunSession.Read(journal));
        Assert.True(File.Exists(journal)); Assert.Equal("source", File.ReadAllText(source));
    }

    [Fact]
    public async Task DestinationDeletionAndNestedOrDuplicateOutputPlansAreRejected()
    {
        string first = TestMediaFixture.NewFolder(), second = TestMediaFixture.NewFolder(), destination = TestMediaFixture.NewFolder();
        TestMediaFixture.Seed(first, OperationId.ZipToBin, "keep"); TestMediaFixture.Seed(second, OperationId.ZipToBin, "keep");
        var cli = App();
        Assert.Equal(2, await cli.RunAsync(["run", "zip-to-audiog", first, "--output", destination, "--delete-originals"], new StringWriter(), new StringWriter()));
        Assert.Equal(2, await cli.RunAsync(["run", "zip-to-audiog", first, "--output", first], new StringWriter(), new StringWriter()));
        Assert.Equal(1, await cli.RunAsync(["run", "zip-to-audiog", first, second, "--output", destination, "--no-cue-text"], new StringWriter(), new StringWriter()));
        Assert.Empty(Directory.GetFileSystemEntries(destination));
    }

    [Theory]
    [InlineData("group-by-alphabet-files")]
    [InlineData("group-by-alphabet-folders")]
    [InlineData("flatten-one-folder-level")]
    [InlineData("flatten-all-folder-levels")]
    [InlineData("clean-up-filenames")]
    [InlineData("add-track-id-separator")]
    [InlineData("remove-track-id-separator")]
    [InlineData("create-cue-txt-files")]
    public async Task FolderToolDryRunsDoNotCreateOrMoveFiles(string operation)
    {
        string root = TestMediaFixture.NewFolder();
        string child = Directory.CreateDirectory(Path.Combine(root, "child")).FullName;
        TestMediaFixture.Seed(child, OperationId.AudioGToBin, "keep");
        File.WriteAllText(Path.Combine(child, "ABC123 - Artist - Title.mp3"), "fixture");
        File.WriteAllText(Path.Combine(child, "ABC-124 - Artist - Title.mp3"), "fixture");
        var before = Snapshot(root); string[] directories = Directory.GetDirectories(root, "*", SearchOption.AllDirectories);
        Assert.Equal(0, await App().RunAsync(["run", operation, root, "--dry-run", "--quiet"], new StringWriter(), new StringWriter()));
        Assert.Equal(before, Snapshot(root)); Assert.Equal(directories, Directory.GetDirectories(root, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task QuarantineCanBePreviewedThenRestoredFromItsManifest()
    {
        string root = TestMediaFixture.NewFolder(); string bad = Path.Combine(root, "bad.zip"); File.WriteAllText(bad, "broken archive");
        var cli = App();
        Assert.Equal(0, await cli.RunAsync(["quarantine", root, "--dry-run", "--quiet"], new StringWriter(), new StringWriter()));
        Assert.True(File.Exists(bad)); Assert.False(Directory.Exists(Path.Combine(root, "Damaged")));
        Assert.Equal(0, await cli.RunAsync(["quarantine", root, "--quiet"], new StringWriter(), new StringWriter()));
        Assert.False(File.Exists(bad)); string manifest = Assert.Single(Directory.GetFiles(root, "manifest.json", SearchOption.AllDirectories));
        Assert.Equal(0, await cli.RunAsync(["run", "restore-quarantined-files", manifest, "--dry-run", "--quiet"], new StringWriter(), new StringWriter()));
        Assert.False(File.Exists(bad));
        Assert.Equal(0, await cli.RunAsync(["run", "restore-quarantined-files", manifest, "--quiet"], new StringWriter(), new StringWriter())); Assert.True(File.Exists(bad));
    }

    private static RecipeDefinition Recipe() => new()
    {
        Name = "Extract and check",
        Steps = [new() { OperationId = "ZipToAudioG", OverwritePolicy = OverwritePolicy.Fail, Options = new() { DeleteOldFiles = false, AutoCreateCueText = false } },
                 new() { OperationId = "ValidateKaraoke", OverwritePolicy = OverwritePolicy.Fail }]
    };

    [Fact]
    public async Task RecipeUsesSavedSettingsSupportsDryRunAndRejectsAsk()
    {
        string root = TestMediaFixture.NewFolder(), library = TestMediaFixture.NewFolder(); TestMediaFixture.Seed(root, OperationId.ZipToBin, "keep");
        string file = Path.Combine(library, "recipe.json"); var recipe = Recipe(); File.WriteAllText(file, RecipeJson.Serialize(recipe));
        var cli = App(); var before = Snapshot(root);
        Assert.Equal(0, await cli.RunAsync(["recipe", "validate", file], new StringWriter(), new StringWriter()));
        Assert.Equal(0, await cli.RunAsync(["recipe", "run", file, root, "--dry-run", "--json"], new StringWriter(), new StringWriter()));
        Assert.Equal(before, Snapshot(root));
        Assert.Equal(0, await cli.RunAsync(["recipe", "run", file, root, "--quiet"], new StringWriter(), new StringWriter()));
        Assert.True(File.Exists(Path.Combine(root, "song.wav"))); Assert.True(File.Exists(Path.Combine(root, "song.zip")));
        Assert.Equal(2, await cli.RunAsync(["recipe", "run", file, root, "--delete-originals"], new StringWriter(), new StringWriter()));
        File.WriteAllText(file, RecipeJson.Serialize(recipe with { Steps = [recipe.Steps[0] with { OverwritePolicy = OverwritePolicy.Ask }] }));
        Assert.Equal(0, await cli.RunAsync(["recipe", "show", file], new StringWriter(), new StringWriter()));
        Assert.Equal(2, await cli.RunAsync(["recipe", "validate", file], new StringWriter(), new StringWriter()));
    }

    [Fact]
    public async Task RecipeRecoveryUsesJournalSnapshotWithoutRecipeFile()
    {
        string root = TestMediaFixture.NewFolder(); TestMediaFixture.Seed(root, OperationId.ZipToBin, "keep");
        var store = new RecoveryStore(TestMediaFixture.NewFolder()); string journal;
        using (var workflow = new WorkflowSession(store, new WorkflowData
        { Kind = RecoveryKind.Recipe, Roots = [root], Inputs = [root], CurrentInputs = [root], Recipe = Recipe() })) journal = workflow.Journal;
        Assert.Equal(0, await App(store).RunAsync(["recover", journal, "--quiet"], new StringWriter(), new StringWriter()));
        Assert.True(File.Exists(Path.Combine(root, "song.wav"))); Assert.Empty(store.Find());
    }

    [Fact]
    public async Task DiscCommandsUseReaderBoundaryAndRequireExperimentalOptIn()
    {
        var reader = new DiscRipTests.FakeReader(); reader.Disc = reader.Disc with { Device = "test-device" };
        var cli = new CliApplication(discReader: reader); string destination = TestMediaFixture.NewFolder();
        Assert.Equal(0, await cli.RunAsync(["disc-list", "--json"], new StringWriter(), new StringWriter()));
        string[] args = ["rip-cdg", "--device", "test-device", "--output", destination, "--disc-name", "TEST", "--tracks", "1", "--format", "Bin", "--no-cue-text", "--quiet"];
        Assert.Equal(2, await cli.RunAsync(args, new StringWriter(), new StringWriter())); Assert.Empty(reader.ReadTracks);
        Assert.Equal(0, await cli.RunAsync([.. args, "--experimental", "--dry-run"], new StringWriter(), new StringWriter())); Assert.Empty(reader.ReadTracks);
        Assert.Equal(0, await cli.RunAsync([.. args, "--experimental"], new StringWriter(), new StringWriter())); Assert.Equal(new[] { 1 }, reader.ReadTracks);
        Assert.True(File.Exists(Path.Combine(destination, "TEST-01.bin")));
        Assert.Equal(1, await cli.RunAsync([.. args, "--experimental", "--dry-run"], new StringWriter(), new StringWriter()));
    }

    [Theory]
    [InlineData("1-100")]
    [InlineData("1,1")]
    [InlineData("99")]
    public async Task InvalidDiscSelectionsCannotReadTracks(string selection)
    {
        var reader = new DiscRipTests.FakeReader(); reader.Disc = reader.Disc with { Device = "test" };
        string root = TestMediaFixture.NewFolder();
        int code = await new CliApplication(discReader: reader).RunAsync(["rip-cdg", "--device", "test", "--output", root,
            "--disc-name", "TEST", "--tracks", selection, "--experimental", "--quiet"], new StringWriter(), new StringWriter());
        Assert.Equal(2, code); Assert.Empty(reader.ReadTracks); Assert.Empty(Directory.GetFileSystemEntries(root));
    }

    [Theory]
    [InlineData("powershell")]
    [InlineData("bash")]
    [InlineData("zsh")]
    public async Task ShellSetupPrintsCommandsWithoutEditingProfiles(string shell)
    {
        var output = new StringWriter(); var cli = App();
        Assert.Equal(0, await cli.RunAsync(["completion", shell], output, new StringWriter()));
        Assert.Contains("karaoke-toolkit", output.ToString()); Assert.Contains("--dry-run", output.ToString());
        output.GetStringBuilder().Clear();
        Assert.Equal(0, await cli.RunAsync(["path", shell], output, new StringWriter())); Assert.Contains("PATH", output.ToString());
        output.GetStringBuilder().Clear();
        Assert.Equal(0, await cli.RunAsync(["run", "karaoke-to-video", "--help"], output, new StringWriter()));
        Assert.Contains("Core ID: KaraokeToVideo", output.ToString());
    }
}
