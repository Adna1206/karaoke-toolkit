using KaraokeToolkit.Core.Execution;
using KaraokeToolkit.App.Features.Main;
using KaraokeToolkit.App.Metadata;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Recipes;

namespace KaraokeToolkit.Tests.App.Recipes;

public class HomeRecipeTests
{
    private static RecipeDefinition Recipe(string operation, bool delete = false) => new()
    {
        Name = "Saved workflow", Steps = [new() { OperationId = operation,
            Options = new() { DeleteOldFiles = delete, AutoCreateCueText = false }, OverwritePolicy = OverwritePolicy.Fail }]
    };

    [Fact]
    public async Task SavedRecipeAppearsOnHomeAndStartUsesItsOwnSettings()
    {
        string directory = TestMediaFixture.NewFolder();
        var main = new MainViewModel(Path.Combine(directory, "settings.xml"));
        Assert.DoesNotContain(main.VisibleSections, section => section.Section == OperationSection.Recipes);
        var recipe = Recipe("ZipToAudioG");
        new RecipeStore(main.RecipeDirectory).Save(recipe);
        main.ReloadRecipes();
        main.SelectedSection = main.VisibleSections.Single(section => section.Section == OperationSection.Recipes);
        var choice = Assert.Single(main.VisibleOperations);
        Assert.Same(choice, main.SelectedOperation); Assert.True(choice.IsRecipe); Assert.False(choice.IsValidation);
        Assert.Contains("1 step:", main.ActionDescription); Assert.True(main.CanChooseFiles);
        TestMediaFixture.Seed(directory, OperationId.ZipToBin, "normal");
        string source = Path.Combine(directory, "song.zip");
        main.SetInputs([source]);
        Assert.Same(choice, main.SelectedOperation); Assert.True(main.CanStart);
        Assert.True(main.Options.DeleteOldFiles);
        await main.StartCommand.ExecuteAsync(null);
        Assert.True(File.Exists(source)); // The recipe keeps originals despite the general deletion setting.
        Assert.True(File.Exists(Path.ChangeExtension(source, ".cdg")));
        Assert.Equal(RunStatus.Success, main.StatusOutcome);
        Assert.Equal("Recipe Report", main.LastReport!.ReportType);
        Assert.False(main.CanStart); Assert.False(main.IsBusy);
    }

    [Fact]
    public void RecipesCanBeFavouritedAndLibraryRefreshHandlesRenameAndDeletion()
    {
        string path = Path.Combine(TestMediaFixture.NewFolder(), "settings.xml");
        var main = new MainViewModel(path);
        var recipe = Recipe("ValidateKaraoke");
        var store = new RecipeStore(main.RecipeDirectory); store.Save(recipe); main.ReloadRecipes();
        var choice = Assert.Single(main.RecipeChoices); main.SelectedOperation = choice;
        choice.ToggleFavouriteCommand.Execute(null);
        Assert.Contains(choice, main.FavouriteOperations);
        var reopened = new MainViewModel(path);
        Assert.True(Assert.Single(reopened.RecipeChoices).IsFavourite);
        store.Save(recipe with { Name = "Renamed workflow" }); main.ReloadRecipes();
        Assert.Equal("Renamed workflow", main.SelectedOperation!.Label);
        Assert.Equal(recipe.Id, main.SelectedOperation.Recipe!.Id); Assert.True(main.SelectedOperation.IsFavourite);
        store.Delete(recipe.Id); main.ReloadRecipes();
        Assert.Empty(main.RecipeChoices); Assert.Empty(main.FavouriteOperations);
        Assert.DoesNotContain(main.VisibleSections, section => section.Section == OperationSection.Recipes);
        Assert.False(main.SelectedOperation!.IsRecipe); Assert.Contains(main.SelectedSection, main.VisibleSections);
    }

    [Fact]
    public void InputControlsAndSelectionChecksFollowFirstStep()
    {
        string directory = TestMediaFixture.NewFolder(), zip = Path.Combine(directory, "song.zip"), audio = Path.Combine(directory, "song.mp3");
        File.WriteAllText(zip, "selection only"); File.WriteAllText(audio, "selection only");
        var main = new MainViewModel(Path.Combine(directory, "settings.xml"));
        var store = new RecipeStore(main.RecipeDirectory);
        var folders = Recipe("FlattenAllLayers"); var archives = Recipe("ZipToAudioG");
        store.Save(folders); store.Save(archives); main.ReloadRecipes();
        main.SelectedOperation = main.RecipeChoices.Single(choice => choice.Recipe!.Id == folders.Id);
        Assert.False(main.CanChooseFiles); Assert.Equal("Source folders", main.SourceLabel);
        Assert.Throws<ArgumentException>(() => main.SetInputs([zip]));
        main.SetInputs([directory]); Assert.True(main.CanStart);
        main.SelectedOperation = main.RecipeChoices.Single(choice => choice.Recipe!.Id == archives.Id);
        Assert.True(main.CanChooseFiles); Assert.Equal("Source files or folders", main.SourceLabel);
        Assert.Equal(new[] { ".zip" }, RecipeOperations.FileExtensions("ZipToAudioG"));
        main.FolderPath = audio; Assert.False(main.CanStart);
        Assert.Throws<ArgumentException>(() => main.SetInputs([audio]));
        main.SetInputs([zip]); Assert.True(main.CanStart);
    }

    [Fact]
    public void OrganizeFilesRecipeAcceptsArbitraryFilesAndRefreshUpdatesChangedFirstStep()
    {
        string directory = TestMediaFixture.NewFolder(), file = Path.Combine(directory, "notes.txt"); File.WriteAllText(file, "fixture");
        var main = new MainViewModel(Path.Combine(directory, "settings.xml"));
        var recipe = Recipe("OrganizeFiles"); var store = new RecipeStore(main.RecipeDirectory);
        store.Save(recipe); main.ReloadRecipes(); main.SelectedOperation = Assert.Single(main.RecipeChoices);
        main.SetInputs([file]); Assert.True(main.CanStart); Assert.Null(RecipeOperations.FileExtensions("OrganizeFiles"));
        store.Save(recipe with { Steps = [new() { OperationId = "FlattenAllLayers" }] }); main.ReloadRecipes();
        Assert.Equal(recipe.Id, main.SelectedOperation!.Recipe!.Id);
        Assert.False(main.CanChooseFiles); Assert.False(main.CanStart);
    }
}
