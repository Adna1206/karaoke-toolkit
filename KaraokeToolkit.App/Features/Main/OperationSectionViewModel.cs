using System.Collections.Generic;
using System.ComponentModel;
using KaraokeToolkit.App.Metadata;
using CommunityToolkit.Mvvm.ComponentModel;

namespace KaraokeToolkit.App.Features.Main;

/// <summary>One category in the operation picker.</summary>
public class OperationSectionViewModel : ObservableObject
{
    private readonly MainViewModel owner;
    public OperationSection Section { get; }
    public string Header => Section.Header;
    private readonly IReadOnlyList<ActionChoice> operations;
    public IReadOnlyList<ActionChoice> Operations => Section == OperationSection.Favourites ? owner.FavouriteOperations
        : Section == OperationSection.Recipes ? owner.RecipeChoices
        : operations;
    public bool IsVisible => (Section != OperationSection.Favourites && Section != OperationSection.Recipes) || Operations.Count > 0;

    public OperationSectionViewModel(MainViewModel owner, OperationSection section, IReadOnlyList<ActionChoice> operations)
    {
        this.owner = owner; Section = section; this.operations = operations;
        owner.PropertyChanged += OnOwnerChanged;
    }

    private void OnOwnerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.FavouriteOperations) && Section == OperationSection.Favourites)
        {
            OnPropertyChanged(nameof(Operations));
            OnPropertyChanged(nameof(IsVisible));
        }
        if (e.PropertyName == nameof(MainViewModel.RecipeChoices) && Section == OperationSection.Recipes)
        {
            OnPropertyChanged(nameof(Operations));
            OnPropertyChanged(nameof(IsVisible));
        }
    }
}
