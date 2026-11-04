using KaraokeToolkit.Core.Execution;
using System.IO.Compression;
using System.Text.Json;
using KaraokeToolkit.Core.Archives;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Recipes;
using KaraokeToolkit.Core.Recovery;
using Action = KaraokeToolkit.Core.Execution.OperationId;

namespace KaraokeToolkit.Tests.Recipes;

public class RecipeTests
{
    private static RecipeStep Step(string operation, int level = 5) => new()
    {
        OperationId = operation, Options = new() { ZipCompression = (ZipCompression)level, AutoCreateCueText = false },
        OverwritePolicy = OverwritePolicy.Fail
    };
    private static RecipeDefinition Recipe(params RecipeStep[] steps) => new() { Name = "My workflow", Steps = steps };
    private static string FileInput(string extension = ".zip")
    {
        string path = Path.Combine(TestMediaFixture.NewFolder(), "song" + extension);
        File.WriteAllText(path, "fixture"); return path;
    }

    [Fact]
    public void PortableRecipeRoundTripsAllStepPreferencesAndLibraryUpdatesById()
    {
        var recipe = Recipe(Step("ZipToAudioG", 0), Step("AudioGToZip", 9) with
        {
            Options = new() { ZipCompression = ZipCompression.SmallestSize, AudioBitrate = 256, DeleteOldFiles = false },
            ValidateBeforeConversion = true, IgnoreLeadingThe = true, OverwritePolicy = OverwritePolicy.Replace
        });
        string json = RecipeJson.Serialize(recipe);
        var restored = RecipeJson.Deserialize(json);
        Assert.Equal(recipe.Id, restored.Id);
        Assert.Equal(recipe.Steps, restored.Steps);
        Assert.DoesNotContain("Inputs", json);
        var store = new RecipeStore(TestMediaFixture.NewFolder());
        store.Save(restored); store.Save(restored with { Name = "Renamed" });
        Assert.Equal("Renamed", Assert.Single(store.Load()).Name);
    }

    [Theory]
    [InlineData("KaraokeToVideo", "ZipToBin")]
    [InlineData("AudioGToZip", "AudioGToBin")]
    [InlineData("ZipToAudioG", "CleanUpFilenames")]
    [InlineData("BinToZip", "FlattenAllLayers")]
    public void KnownIncompatibleStepsAreRejected(string first, string second)
        => Assert.Throws<ArgumentException>(() => RecipeOperations.Validate(Recipe(Step(first), Step(second))));

    [Fact]
    public void ValidationPreservesFormatConstraintsAndUnsupportedImportsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => RecipeOperations.Validate(Recipe(Step("AudioGToZip"), Step("ValidateKaraoke"), Step("AudioGToBin"))));
        Assert.Throws<NotSupportedException>(() => RecipeOperations.Validate(Recipe(Step("DiscRip"))));
        Assert.Throws<NotSupportedException>(() => RecipeOperations.Validate(Recipe(Step("ValidateKaraoke")) with { FormatVersion = 2 }));
        Assert.Throws<JsonException>(() => RecipeJson.Deserialize("{\"Name\":\"Test\",\"Script\":\"unexpected\"}"));
    }

    [Fact]
    public async Task ChainingUsesPrimaryOutputsAndEachStepsOwnSettings()
    {
        string source = FileInput(), folder = Path.GetDirectoryName(source)!;
        string audio = Path.Combine(folder, "song.mp3"), graphics = Path.Combine(folder, "song.cdg"), listing = Path.Combine(folder, "tracks.txt");
        foreach (string file in new[] { audio, graphics, listing }) File.WriteAllText(file, "fixture");
        var executor = new FakeExecutor((_, _, call) => new([new(source, ConversionStatus.Succeeded,
            call == 1 ? [audio, graphics, listing] : [source], "done")], false));
        var result = await new RecipeRunner(executor).RunAsync(Recipe(Step("ZipToAudioG", 0), Step("AudioGToZip", 9)), [source]);
        Assert.Equal(new[] { audio, graphics }, executor.Calls[1].Inputs);
        Assert.Equal(0, (int)executor.Calls[0].Step.Options.ZipCompression);
        Assert.Equal(9, (int)executor.Calls[1].Step.Options.ZipCompression);
        Assert.DoesNotContain(source, executor.Calls[1].Inputs);
        Assert.Single(result.Outputs);
        Assert.Empty(result.StopReason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailureAndCancellationStopLaterSteps(bool cancelled)
    {
        string source = FileInput();
        var executor = new FakeExecutor((_, _, _) => new([new(source, cancelled ? ConversionStatus.Cancelled : ConversionStatus.Failed, [], "stopped")], cancelled));
        var result = await new RecipeRunner(executor).RunAsync(Recipe(Step("ZipToAudioG"), Step("AudioGToZip")), [source]);
        Assert.Single(executor.Calls);
        Assert.Single(result.Steps);
        Assert.Equal(cancelled, result.Cancelled);
        Assert.NotEmpty(result.StopReason);
        Assert.Equal("Recipe Report", result.ToReport("My workflow").ReportType);
    }

    [Fact]
    public async Task CancellationBeforeStartingRunsNoOperations()
    {
        var executor = new FakeExecutor((_, _, _) => throw new InvalidOperationException("Must not execute"));
        var result = await new RecipeRunner(executor).RunAsync(Recipe(Step("ValidateKaraoke")), [FileInput()], token: new CancellationToken(true));
        Assert.True(result.Cancelled); Assert.Empty(executor.Calls);
    }

    [Fact]
    public async Task EmptyOrSkippedConversionDoesNotFeedOriginalsToNextStep()
    {
        string source = FileInput();
        var executor = new FakeExecutor((_, _, _) => new([new(source, ConversionStatus.Skipped, [], "exists")], false));
        var result = await new RecipeRunner(executor).RunAsync(Recipe(Step("ZipToAudioG"), Step("AudioGToZip")), [source]);
        Assert.Single(executor.Calls); Assert.Empty(result.Outputs); Assert.NotEmpty(result.StopReason);
    }

    [Fact]
    public async Task ValidationAndFolderMaintenancePassTheirInputScopeOnward()
    {
        string folder = TestMediaFixture.NewFolder();
        var executor = new FakeExecutor((_, _, _) => new([new(folder, ConversionStatus.Succeeded, [], "checked")], false));
        await new RecipeRunner(executor).RunAsync(Recipe(Step("FlattenAllLayers"), Step("ValidateKaraoke")), [folder]);
        Assert.Equal(new[] { folder }, executor.Calls[1].Inputs);
    }

    [Fact]
    public async Task LosslessStepRetainsGraphicsCompanionAndOrganizingMixedInputsTracksMovedFiles()
    {
        string graphics = FileInput(".cdg"), folder = Path.GetDirectoryName(graphics)!;
        string audio = Path.ChangeExtension(graphics, ".mp3"); File.WriteAllText(audio, "fixture");
        var lossless = new FakeExecutor((_, _, call) => new([new(graphics, ConversionStatus.Succeeded, call == 1 ? [audio] : [], "done")], false));
        await new RecipeRunner(lossless).RunAsync(Recipe(Step("LosslessGToMp3G"), Step("ValidateKaraoke")), [graphics]);
        Assert.Contains(graphics, lossless.Calls[1].Inputs); Assert.Contains(audio, lossless.Calls[1].Inputs);

        string otherFolder = TestMediaFixture.NewFolder(), moved = Path.Combine(folder, "moved.cdg");
        var organizer = new FakeExecutor((_, _, call) =>
        {
            if (call == 1) File.Move(graphics, moved);
            return new([new(graphics, ConversionStatus.Succeeded, [moved], "moved")], false);
        });
        await new RecipeRunner(organizer).RunAsync(Recipe(Step("OrganizeFiles"), Step("ValidateKaraoke")), [graphics, otherFolder]);
        Assert.Equal(new[] { moved, otherFolder }, organizer.Calls[1].Inputs);
    }

    [Fact]
    public async Task InvalidPipelineIsRejectedBeforeExecutorRuns()
    {
        var executor = new FakeExecutor((_, _, _) => throw new InvalidOperationException("Must not execute"));
        await Assert.ThrowsAsync<ArgumentException>(() => new RecipeRunner(executor).RunAsync(Recipe(Step("KaraokeToVideo"), Step("ZipToBin")), [FileInput()]));
        Assert.Empty(executor.Calls);
    }

    [Fact]
    public async Task RealExtractionPackagingAndValidationUseOnlySelectedTrack()
    {
        string folder = TestMediaFixture.NewFolder();
        TestMediaFixture.Seed(folder, Action.ZipToBin, "normal");
        string source = Path.Combine(folder, "song.zip"), neighbour = Path.Combine(folder, "other.zip");
        File.Copy(source, neighbour);
        byte[] before = File.ReadAllBytes(neighbour);
        var recipe = Recipe(Step("ZipToAudioG"), Step("AudioGToZip", 0) with { Options = new() { ZipCompression = ZipCompression.NoCompression, DeleteOldFiles = false, AutoCreateCueText = true } }, Step("ValidateKaraoke"));
        var result = await new RecipeRunner(new RecipeStepExecutor(new RecoveryStore(TestMediaFixture.NewFolder()))).RunAsync(recipe, [source]);
        Assert.True(result.StopReason.Length == 0, string.Join("; ", result.Steps.SelectMany(step => step.Result.Files.Select(file => file.Message))));
        Assert.Equal(3, result.Steps.Count);
        Assert.Equal(source, Assert.Single(result.Outputs));
        Assert.Equal(before, File.ReadAllBytes(neighbour));
        Assert.True(File.Exists(Path.Combine(folder, "song.wav")));
        Assert.True(File.Exists(Path.Combine(folder, "song.cdg")));
        using var archive = ZipFile.OpenRead(source);
        Assert.Equal(2, archive.Entries.Count);
        Assert.All(result.Steps, step => Assert.Equal(0, step.Result.Failed));
    }

    private sealed class FakeExecutor(Func<RecipeStep, IReadOnlyList<string>, int, ConversionResult> run) : IRecipeStepExecutor
    {
        public List<(RecipeStep Step, string[] Inputs)> Calls { get; } = [];
        public Task<ConversionResult> ExecuteAsync(RecipeStep step, IReadOnlyList<string> inputs, IProgress<ConversionProgress>? progress, CancellationToken token)
        {
            Calls.Add((step, inputs.ToArray()));
            return Task.FromResult(run(step, inputs, Calls.Count));
        }
    }
}
