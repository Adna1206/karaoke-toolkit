using System.IO.Compression;
using KaraokeToolkit.App.Features.Recipes;
using KaraokeToolkit.App.Metadata;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Recipes;
using KaraokeToolkit.Core.Recovery;
using KaraokeToolkit.Core.Tagging;
using Action = KaraokeToolkit.Core.Conversion.OperationCatalog.defAction;

namespace KaraokeToolkit.Tests.Recipes;

public class RecipeTaggingTests
{
    [Fact]
    public void EditorAndPortableRecipePreserveTaggingSettingsIndependently()
    {
        var original = new RecipeStep { OperationId = "TagKaraoke" };
        var editor = new RecipeStepOptionsViewModel(original)
        {
            TaggingPattern = "%ARTIST% . %TITLE%", FillEmptyTagsOnly = true,
            ClearTagComments = false, ClearOtherTags = false, RecurseFolders = false, CompressionLevel = 9
        };
        Assert.True(editor.HasTaggingSettings); Assert.True(editor.HasZipSettings); Assert.True(editor.HasRecursionSettings);
        Assert.False(editor.CanDeleteOriginals); Assert.False(editor.HasOverwriteSettings);
        var step = editor.ToStep(original);
        var recipe = new RecipeDefinition { Steps = [step] };
        var restored = RecipeJson.Deserialize(RecipeJson.Serialize(recipe));
        Assert.Equal(step, Assert.Single(restored.Steps));
        Assert.Equal(FilenameTagPattern.Default, original.TaggingPattern); Assert.False(original.Tagging.FillEmptyOnly);
        Assert.True(step.Tagging.FillEmptyOnly); Assert.False(step.Tagging.ClearComments);
        Assert.Equal(9, (int)step.Options.ZipCompression); Assert.False(step.Options.RecurseFolders);
        Assert.False(new RecipeStepOptionsViewModel(new() { OperationId = "ValidateKaraoke" }).HasTaggingSettings);
    }

    [Fact]
    public void EligibilityAndFormatChecksUseCoreCapabilities()
    {
        Assert.True(OperationDefinitions.All.Single(o => o.Id == "TagKaraoke").CanUseInRecipes);
        foreach (var definition in OperationDefinitions.All)
            Assert.Equal(RecipeOperations.Supports(definition.Id), definition.CanUseInRecipes);
        Assert.Throws<ArgumentException>(() => RecipeOperations.Validate(new() { Steps = [new() { OperationId = "TagKaraoke", TaggingPattern = "%UNKNOWN%" }] }));
        Assert.Throws<ArgumentException>(() => RecipeOperations.Validate(new() { Steps = [new() { OperationId = "Zip_To_Bin" }, new() { OperationId = "TagKaraoke" }] }));
        Assert.Throws<ArgumentException>(() => RecipeOperations.Validate(new() { Steps = [new() { OperationId = "Karaoke_To_Video" }, new() { OperationId = "TagKaraoke" }, new() { OperationId = "AudioG_To_Zip" }] }));
    }

    [Fact]
    public async Task TagsAudioThenCreatesZipAndTagsZipWithoutChangingGraphics()
    {
        string folder = TestMediaFixture.NewFolder(); TestMediaFixture.Seed(folder, Action.AudioG_To_Bin, "keep");
        string audio = Path.Combine(folder, "DISC-00 - Artist - Title.wav"), graphics = Path.ChangeExtension(audio, ".cdg");
        File.Move(Path.Combine(folder, "song.wav"), audio); File.Move(Path.Combine(folder, "song.cdg"), graphics);
        string graphicsHash = FileTransaction.Hash(graphics);
        var recipe = new RecipeDefinition { Steps = [
            new() { OperationId = "TagKaraoke", Tagging = new(ClearComments: false) },
            new() { OperationId = "AudioG_To_Zip", Options = new() { DeleteOldFiles = false, AutoCreateCueText = false }, OverwritePolicy = OverwritePolicy.Fail },
            new() { OperationId = "TagKaraoke", TaggingPattern = "%TITLE%", Tagging = new(FillEmptyOnly: true), Options = new() { ZipCompression = KaraokeToolkit.Core.Archives.ZipCompression.SmallestSize } }
        ] };
        var store = new RecoveryStore(TestMediaFixture.NewFolder());
        var result = await new RecipeRunner(recovery: store).RunAsync(recipe, [audio, graphics]);
        Assert.Empty(result.StopReason); Assert.Equal(3, result.Steps.Count);
        Assert.All(result.Steps, step => Assert.Equal(1, step.Result.Succeeded));
        string zip = Assert.Single(result.Outputs); Assert.Equal(Path.ChangeExtension(audio, ".zip"), zip);
        string extracted = TestMediaFixture.NewFolder(); ZipFile.ExtractToDirectory(zip, extracted);
        var tags = KaraokeTaggingService.ReadTags(Assert.Single(Directory.GetFiles(extracted, "*.wav")));
        Assert.Equal("DISC", tags.Album); Assert.Equal("Artist", tags.Artist); Assert.Equal("Title", tags.Title);
        Assert.Equal(graphicsHash, FileTransaction.Hash(Assert.Single(Directory.GetFiles(extracted, "*.cdg"))));
        Assert.Equal(graphicsHash, FileTransaction.Hash(graphics)); Assert.Empty(store.Find());
    }

    [Fact]
    public async Task FilenameMismatchFailsRecipeBeforeFollowingStepAndLeavesSourceUntouched()
    {
        string folder = TestMediaFixture.NewFolder(); TestMediaFixture.Seed(folder, Action.AudioG_To_Bin, "keep");
        string audio = Path.Combine(folder, "song.wav"), hash = FileTransaction.Hash(audio);
        var recipe = new RecipeDefinition { Steps = [new() { OperationId = "TagKaraoke" }, new() { OperationId = "AudioG_To_Zip" }] };
        var result = await new RecipeRunner(recovery: new(TestMediaFixture.NewFolder())).RunAsync(recipe, [folder]);
        Assert.Single(result.Steps); Assert.Equal(1, result.Steps[0].Result.Failed); Assert.NotEmpty(result.StopReason);
        Assert.Equal(hash, FileTransaction.Hash(audio)); Assert.False(File.Exists(Path.Combine(folder, "song.zip")));
    }
}
