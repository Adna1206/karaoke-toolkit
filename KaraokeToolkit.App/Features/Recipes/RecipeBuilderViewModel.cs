using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KaraokeToolkit.App.Features.Main;
using KaraokeToolkit.App.Metadata;
using KaraokeToolkit.App.Shared;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Recipes;

namespace KaraokeToolkit.App.Features.Recipes;

public sealed partial class RecipeStepItem(RecipeStep step) : ObservableObject
{
    public RecipeStep Step { get; } = step;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(DisplayLabel))]
    public partial int Number { get; set; }
    public OperationDefinition Definition => OperationDefinitions.All.Single(operation => operation.Id == Step.OperationId);
    public string Label => Definition.Label;
    public string DisplayLabel => $"Step {Number}: {Label}";
    public string Summary => Step.OperationId switch
    {
        "OrganizeFiles" or "OrganizeFolders" or "FlattenOneLayer" or "FlattenAllLayers" => Definition.ShortDescription,
        "ValidateKaraoke" or "CleanUpFilenames" or "AddTrackIdSeparator" or "RemoveTrackIdSeparator" or "CreateCueAndTextFiles"
            => Step.Options.RecurseFolders ? "Includes subfolders" : "Selected inputs only",
        "KaraokeToVideo" => $"{Step.Options.VideoContainer} · {Step.Options.VideoResolution} · {Step.Options.VideoFrameRate} fps",
        "TagKaraoke" => $"{Step.TaggingPattern} · {(Step.Tagging.FillEmptyOnly ? "fill empty fields" : "replace tags")}",
        "LosslessGToMp3G" => $"MP3 · {Step.Options.AudioBitrate} kbps · originals {(Step.Options.DeleteOldFiles ? "deleted" : "kept")}",
        "BinToAudioG" => $"{Step.Options.AudioEncoder} · {Step.Options.AudioBitrate} kbps · originals {(Step.Options.DeleteOldFiles ? "deleted" : "kept")}",
        "ZipToAudioG" or "ZipToBin" or "AudioGToBin" => $"Originals {(Step.Options.DeleteOldFiles ? "deleted" : "kept")} · existing outputs: {Step.OverwritePolicy}",
        _ => $"{Step.Options.ZipAlgorithm}, level {(int)Step.Options.ZipCompression} · originals {(Step.Options.DeleteOldFiles ? "deleted" : "kept")}"
    };
}

public partial class RecipeBuilderViewModel : ViewModelBase
{
    private readonly RecipeStore store;
    private readonly RecipeStep defaults;
    private readonly int mp3Bitrate;
    private Guid id = Guid.NewGuid();
    public ObservableCollection<RecipeDefinition> SavedRecipes { get; } = [];
    public ObservableCollection<RecipeStepItem> Steps { get; } = [];
    public IReadOnlyList<OperationDefinition> Operations { get; } = OperationDefinitions.All
        .Where(definition => definition.CanUseInRecipes).ToArray();
    [ObservableProperty] public partial string Name { get; set; } = "New recipe";
    [ObservableProperty] public partial string Error { get; set; } = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanDeleteRecipe))]
    public partial RecipeDefinition? SelectedSavedRecipe { get; set; }
    [ObservableProperty] public partial OperationDefinition? SelectedOperation { get; set; }
    [ObservableProperty] public partial RecipeStepItem? SelectedStep { get; set; }
    public string StepDescription => SelectedStep?.Definition.Description ?? "Select a step to inspect or configure it.";
    public bool HasSelectedStep => SelectedStep != null;
    public bool CanDeleteRecipe => SelectedSavedRecipe != null;
    public bool CanSave
    {
        get { try { RecipeOperations.Validate(Definition()); return true; } catch { return false; } }
    }
    public RecipeBuilderViewModel(MainViewModel main) : this(main.RecipeDirectory,
        new RecipeStep { Options = main.Options, OverwritePolicy = main.OverwritePolicy,
            ValidateBeforeConversion = main.ValidateBeforeConversion, IgnoreLeadingThe = main.IgnoreLeadingTheForFileOrganization },
        main.AudioBitrates.Mp3) { }

    internal RecipeBuilderViewModel(string directory, RecipeStep defaults, int? mp3Bitrate = null)
    {
        store = new(directory);
        this.defaults = defaults;
        this.mp3Bitrate = mp3Bitrate ?? defaults.Options.AudioBitrate;
        SelectedOperation = Operations.First();
        Steps.CollectionChanged += (_, _) => Refresh();
        try { foreach (var recipe in store.Load()) SavedRecipes.Add(recipe); }
        catch (Exception error) { Error = "Could not load the recipe library: " + error.Message; }
    }
    public RecipeDefinition Definition() => new() { Id = id, Name = Name.Trim(), Steps = Steps.Select(item => item.Step).ToArray() };
    partial void OnNameChanged(string value) => Refresh();
    partial void OnSelectedStepChanged(RecipeStepItem? value)
    {
        OnPropertyChanged(nameof(HasSelectedStep));
        OnPropertyChanged(nameof(StepDescription));
    }
    partial void OnSelectedSavedRecipeChanged(RecipeDefinition? value) { if (value != null) Load(value); }
    public void Load(RecipeDefinition recipe)
    {
        RecipeOperations.Validate(recipe);
        id = recipe.Id;
        Name = recipe.Name;
        Steps.Clear();
        foreach (var step in recipe.Steps) Steps.Add(new(step));
        SelectedStep = Steps.FirstOrDefault();
        Error = "";
    }
    [RelayCommand]
    private void NewRecipe()
    {
        SelectedSavedRecipe = null;
        id = Guid.NewGuid(); Name = "New recipe"; Steps.Clear(); SelectedStep = null; Error = "";
    }
    [RelayCommand]
    private void AddStep()
    {
        if (SelectedOperation == null) return;
        var step = defaults with { OperationId = SelectedOperation.Id };
        if (step.OperationId == "LosslessGToMp3G") step = step with
            { Options = step.Options with { AudioEncoder = KaraokeToolkit.Core.Audio.AudioEncoder.MP3, AudioBitrate = mp3Bitrate } };
        try
        {
            RecipeOperations.Validate(Definition() with { Steps = Steps.Select(item => item.Step).Append(step).ToArray() });
            var item = new RecipeStepItem(step); Steps.Add(item); SelectedStep = item; Error = "";
        }
        catch (Exception error) { Error = error.Message; }
    }
    [RelayCommand]
    private void RemoveStep() { if (SelectedStep != null) { int index = Steps.IndexOf(SelectedStep); Steps.Remove(SelectedStep); SelectedStep = Steps.ElementAtOrDefault(Math.Min(index, Steps.Count - 1)); } }
    [RelayCommand] private void MoveUp() => Move(-1);
    [RelayCommand] private void MoveDown() => Move(1);
    private void Move(int direction)
    {
        if (SelectedStep == null) return;
        var item = SelectedStep;
        int index = Steps.IndexOf(item), destination = index + direction;
        if (destination >= 0 && destination < Steps.Count)
        {
            Steps.Move(index, destination);
            SelectedStep = item;
        }
    }
    public void UpdateStep(RecipeStepItem item, RecipeStep step)
    {
        int index = Steps.IndexOf(item);
        if (index < 0) return;
        Steps[index] = new(step); SelectedStep = Steps[index];
    }
    public void Save()
    {
        var recipe = Definition(); store.Save(recipe);
        var previous = SavedRecipes.FirstOrDefault(saved => saved.Id == recipe.Id);
        if (previous != null) SavedRecipes.Remove(previous);
        SavedRecipes.Add(recipe);
        SelectedSavedRecipe = recipe;
        Error = "Recipe saved.";
    }
    public void DeleteSavedRecipe(Guid recipeId)
    {
        // The confirmation identifies a specific recipe, even if selection changes meanwhile.
        var recipe = SavedRecipes.FirstOrDefault(saved => saved.Id == recipeId)
            ?? throw new ArgumentException("This recipe is not in the saved library.");
        store.Delete(recipeId); // Keep the UI/library unchanged if the filesystem deletion fails.
        bool clearEditor = id == recipeId;
        if (SelectedSavedRecipe?.Id == recipeId) SelectedSavedRecipe = null;
        SavedRecipes.Remove(recipe);
        if (clearEditor) NewRecipe();
        Error = $"Deleted recipe: {recipe.Name}.";
    }
    private void Refresh()
    {
        for (int index = 0; index < Steps.Count; index++) Steps[index].Number = index + 1;
        OnPropertyChanged(nameof(CanSave));
        if (Steps.Count > 0)
        {
            try { RecipeOperations.Validate(Definition()); Error = ""; }
            catch (Exception error) { Error = error.Message; }
        }
    }
}
