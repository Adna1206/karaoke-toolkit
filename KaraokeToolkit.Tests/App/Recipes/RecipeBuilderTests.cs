using KaraokeToolkit.App.Features.Main;
using KaraokeToolkit.App.Features.Recipes;
using KaraokeToolkit.App.Metadata;
using KaraokeToolkit.Core.Archives;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Recipes;

namespace KaraokeToolkit.Tests.App.Recipes;

public class RecipeBuilderTests
{
    [Fact]
    public void EditingSavingAndReloadingStepsKeepsSnapshotsIndependent()
    {
        string directory = TestMediaFixture.NewFolder();
        var defaults = new RecipeStep { Options = new() { ZipCompression = ZipCompression.Level3 } };
        var model = new RecipeBuilderViewModel(directory, defaults);
        model.SelectedOperation = model.Operations.Single(operation => operation.Id == "Zip_To_AudioG");
        model.AddStepCommand.Execute(null);
        var first = Assert.Single(model.Steps);
        model.UpdateStep(first, first.Step with { Options = first.Step.Options with { ZipCompression = ZipCompression.SmallestSize } });
        model.SelectedOperation = model.Operations.Single(operation => operation.Id == "AudioG_To_Zip");
        model.AddStepCommand.Execute(null);
        Assert.Equal(ZipCompression.Level3, defaults.Options.ZipCompression);
        Assert.Equal(ZipCompression.Level3, model.Steps[1].Step.Options.ZipCompression);
        model.Name = "Extract and package"; model.Save();
        var reopened = new RecipeBuilderViewModel(directory, defaults);
        reopened.SelectedSavedRecipe = Assert.Single(reopened.SavedRecipes);
        Assert.Equal(model.Definition().Steps, reopened.Definition().Steps);
        Assert.True(reopened.CanSave);
        Assert.Equal("Extract and package", reopened.Definition().Name);
    }

    [Fact]
    public void BuilderBlocksKnownInvalidStackAndOffersOnlyRegisteredOperations()
    {
        var model = new RecipeBuilderViewModel(TestMediaFixture.NewFolder(), new());
        Assert.All(model.Operations, definition => Assert.True(definition.Available));
        Assert.Equal(RecipeOperations.All.Select(operation => operation.Id).Order(), model.Operations.Select(operation => operation.Id).Order());
        model.SelectedOperation = model.Operations.Single(operation => operation.Id == "Karaoke_To_Video");
        model.AddStepCommand.Execute(null);
        model.SelectedOperation = model.Operations.Single(operation => operation.Id == "Zip_To_Bin");
        model.AddStepCommand.Execute(null);
        Assert.Single(model.Steps);
        Assert.Contains("cannot accept", model.Error);
    }

    [Fact]
    public void DeletingSavedRecipeClearsEditorAndPreservesOtherRecipesAndInputs()
    {
        string directory = TestMediaFixture.NewFolder(), input = Path.Combine(directory, "track.cdg");
        File.WriteAllText(input, "karaoke file");
        var model = new RecipeBuilderViewModel(directory, new());
        model.Load(new() { Name = "Keep", Steps = [new() { OperationId = "ValidateKaraoke" }] }); model.Save();
        Guid keepId = model.Definition().Id;
        model.NewRecipeCommand.Execute(null);
        Assert.False(model.CanDeleteRecipe);
        model.Load(new() { Name = "Remove", Steps = [new() { OperationId = "ValidateKaraoke" }] }); model.Save();
        var removed = model.SelectedSavedRecipe!;
        string exportedCopy = Path.Combine(directory, "exported-copy.txt");
        File.WriteAllText(exportedCopy, RecipeJson.Serialize(removed));
        model.Name = "Unsaved edit";
        Assert.True(model.CanDeleteRecipe);
        model.DeleteSavedRecipe(removed.Id);
        Assert.Null(model.SelectedSavedRecipe); Assert.False(model.CanDeleteRecipe);
        Assert.Empty(model.Steps); Assert.Equal("New recipe", model.Name);
        Assert.Equal(keepId, Assert.Single(model.SavedRecipes).Id);
        Assert.Equal(keepId, Assert.Single(new RecipeStore(directory).Load()).Id);
        Assert.Equal("karaoke file", File.ReadAllText(input)); Assert.True(File.Exists(exportedCopy));
        Assert.False(File.Exists(Path.Combine(directory, removed.Id.ToString("D") + ".json")));
    }

    [Fact]
    public void FailedDeletionKeepsSavedSelectionAndEditor()
    {
        string directory = TestMediaFixture.NewFolder();
        var model = new RecipeBuilderViewModel(directory, new());
        model.Load(new() { Name = "Keep on error", Steps = [new() { OperationId = "ValidateKaraoke" }] }); model.Save();
        var saved = model.SelectedSavedRecipe!;
        string path = Path.Combine(directory, saved.Id.ToString("D") + ".json");
        // A directory at the expected file path cannot be removed by file deletion on any platform.
        File.Delete(path); Directory.CreateDirectory(path);
        Assert.ThrowsAny<Exception>(() => model.DeleteSavedRecipe(saved.Id));
        Assert.Same(saved, model.SelectedSavedRecipe); Assert.Single(model.SavedRecipes);
        Assert.Single(model.Steps); Assert.True(model.CanDeleteRecipe); Assert.True(Directory.Exists(path));
    }

    [Fact]
    public void StepEditorShowsRelevantSettingsAndFixesDedicatedMp3Encoder()
    {
        var validation = new RecipeStepOptionsViewModel(new() { OperationId = "ValidateKaraoke" });
        Assert.True(validation.HasRecursionSettings);
        Assert.False(validation.CanDeleteOriginals); Assert.False(validation.HasZipSettings);
        Assert.False(validation.HasOverwriteSettings); Assert.False(validation.HasVideoSettings);
        var transcode = new RecipeStepOptionsViewModel(new()
            { OperationId = "LosslessG_To_Mp3G", Options = new() { AudioEncoder = KaraokeToolkit.Core.Audio.AudioEncoder.WMA, AudioBitrate = 192 } });
        Assert.True(transcode.HasAudioSettings); Assert.False(transcode.CanChooseEncoder);
        Assert.Equal(KaraokeToolkit.Core.Audio.AudioEncoder.MP3, transcode.ToOptions().AudioEncoder);
        var video = new RecipeStepOptionsViewModel(new() { OperationId = "Karaoke_To_Video" });
        Assert.True(video.HasVideoSettings); Assert.True(video.CanValidateInputs); Assert.False(video.CanDeleteOriginals);
    }

    [Fact]
    public void ReorderingAnInvalidChainDisablesSaveAndRunUntilCorrected()
    {
        string folder = TestMediaFixture.NewFolder();
        var model = new RecipeBuilderViewModel(folder, new());
        model.Load(new() { Name = "Video", Steps = [new() { OperationId = "Bin_To_Zip" }, new() { OperationId = "Karaoke_To_Video" }] });
        model.Save();
        Assert.True(model.CanSave);
        model.SelectedStep = model.Steps[1]; model.MoveUpCommand.Execute(null);
        Assert.False(model.CanSave); Assert.Contains("cannot accept", model.Error);
        Assert.Throws<ArgumentException>(() => model.Save());
        Assert.Equal("Bin_To_Zip", Assert.Single(new RecipeStore(folder).Load()).Steps[0].OperationId);
        model.SelectedStep = model.Steps[0]; model.MoveDownCommand.Execute(null);
        Assert.True(model.CanSave);
    }

    [Fact]
    public void StepNumbersFollowOrderAndIdenticalStepsRemainDistinct()
    {
        var model = new RecipeBuilderViewModel(TestMediaFixture.NewFolder(), new());
        model.Load(new() { Steps = [new() { OperationId = "ValidateKaraoke" }, new() { OperationId = "ValidateKaraoke" }, new() { OperationId = "ValidateKaraoke" }] });
        var first = model.Steps[0]; var second = model.Steps[1]; var third = model.Steps[2];
        Assert.Equal(new[] { 1, 2, 3 }, model.Steps.Select(item => item.Number));
        Assert.StartsWith("Step 1:", first.DisplayLabel);
        model.SelectedStep = third; model.MoveUpCommand.Execute(null);
        Assert.Same(first, model.Steps[0]); Assert.Same(third, model.Steps[1]); Assert.Same(second, model.Steps[2]);
        Assert.Same(third, model.SelectedStep);
        Assert.Equal(2, third.Number); Assert.Equal(3, second.Number);
        model.RemoveStepCommand.Execute(null);
        Assert.Same(second, model.SelectedStep);
        Assert.Equal(new[] { 1, 2 }, model.Steps.Select(item => item.Number));
        Assert.StartsWith("Step 2:", second.DisplayLabel);
    }

    [Fact]
    public async Task MainWindowRecipeExecutionProducesReportAndResettableStatus()
    {
        string directory = TestMediaFixture.NewFolder();
        TestMediaFixture.Seed(directory, OperationCatalog.defAction.Zip_To_Bin, "normal");
        var model = new MainViewModel(Path.Combine(directory, "settings.xml"));
        var options = model.Options;
        await model.RunRecipeAsync(new() { Name = "Check track", Steps = [new() { OperationId = "ValidateKaraoke" }] }, [Path.Combine(directory, "song.zip")]);
        Assert.Equal(RunStatus.Success, model.StatusOutcome);
        Assert.Equal("Recipe Report", model.LastReport!.ReportType);
        Assert.Contains("Step 1", Assert.Single(model.LastReport.Entries).Message);
        Assert.Same(options, model.Options);
        Assert.True(model.ShowProgress); Assert.False(model.IsBusy);
        model.ShowProgress = false; Assert.Equal(RunStatus.Neutral, model.StatusOutcome);
    }
}
