using KaraokeToolkit.Core.Execution;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KaraokeToolkit.App.Metadata;
using KaraokeToolkit.Core.FileOperations;
using KaraokeToolkit.Core.Recipes;
using KaraokeToolkit.Core.Archives;
using KaraokeToolkit.Core.Audio;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Tagging;
using System.Collections.Generic;
using System.Linq;
using Action = KaraokeToolkit.Core.Execution.OperationId;

namespace KaraokeToolkit.App.Features.Main;

/// <summary>User state for an immutable operation definition. Favourites are saved separately by ID.</summary>
public partial class ActionChoice(OperationDefinition definition, RecipeDefinition? recipe = null) : ObservableObject
{
    public RecipeDefinition? Recipe { get; } = recipe;
    public bool IsRecipe => Recipe != null;
    public OperationDefinition Definition { get; } = definition;
    public Action? Action => Definition.Action;
    public string Label => Definition.Label;
    public bool Available => Definition.Available;
    public string Description => Definition.Description;
    public string ShortDescription => Definition.ShortDescription;
    public string Id => Definition.Id;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(FavouriteIcon)), NotifyPropertyChangedFor(nameof(FavouriteHint)), NotifyPropertyChangedFor(nameof(FavouriteAccessibleName))]
    public partial bool IsFavourite { get; set; }
    public string FavouriteIcon => IsFavourite ? "★" : "☆";
    public string FavouriteHint => IsFavourite ? "Remove from favourites" : "Add to favourites";
    public string FavouriteAccessibleName => FavouriteHint + ": " + Label;
    [RelayCommand(CanExecute = nameof(Available))]
    private void ToggleFavourite() => IsFavourite = !IsFavourite;
    public bool IsRip => Definition.IsRip;
    public bool IsRestore => Definition.IsRestore;
    public bool IsTagging => Definition.IsTagging;
    public bool IsAudioGain => Definition.IsAudioGain;
    public bool IsAudioKey => Definition.IsAudioKey;
    public bool IsAudioAdjustment => IsAudioGain || IsAudioKey;
    public bool IsTrackGrouping => Definition.IsTrackGrouping;
    public bool IsFilenameRename => Definition.IsFilenameRename;
    public AlphabeticalOrganizeScope? OrganizeMode => Definition.OrganizeMode;
    public bool IsOrganizer => OrganizeMode.HasValue;
    public FolderFlattenScope? FlattenScope => Definition.FlattenScope;
    public bool IsFlatten => FlattenScope.HasValue;
    public bool IsValidation => Definition.IsValidation;
    // Null means arbitrary files, and an empty list means no files in the source picker.
    public IReadOnlyList<string>? SourceFileExtensions =>
        Definition.Inputs != OperationInputKind.FilesAndFolders ? [] :
        Recipe is { } recipe ? RecipeOperations.FileExtensions(recipe.Steps[0].OperationId) :
        IsOrganizer || IsTrackGrouping ? null :
        IsTagging ? KaraokeTaggingService.InputExtensions :
        IsAudioAdjustment ? AudioGainService.Extensions :
        IsFilenameRename ? FilenameRenameService.Extensions :
        Action is { } action && OperationCapabilities.IsConversion(action) ? InputSelection.ConversionFileExtensions(action) :
        InputSelection.Extensions.Concat(ArchiveZipConverter.Extensions).ToArray();
    public OperationSection Section => Definition.Section;
}
