using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using KaraokeToolkit.App.Metadata;
using CommunityToolkit.Mvvm.ComponentModel;

namespace KaraokeToolkit.App.Features.Main;

/// <summary>One operation category sharing the main window's selection. Legacy expansion state remains compatible.</summary>
public partial class OperationSectionViewModel : ObservableObject
{
    private readonly MainViewModel owner;
    public OperationSection Section { get; }
    public string Header { get; }
    private readonly IReadOnlyList<ActionChoice> operations;
    public IReadOnlyList<ActionChoice> Operations => Section switch
    {
        OperationSection.Favourites => owner.FavouriteOperations,
        OperationSection.Recipes => owner.RecipeChoices,
        _ => operations
    };
    public ActionChoice? SelectedOperation => owner.SelectedOperation;
    public bool IsVisible => Section is not (OperationSection.Favourites or OperationSection.Recipes) || Operations.Count > 0;
    private bool ContainsSelection => owner.SelectedOperation != null && Operations.Contains(owner.SelectedOperation);
    [ObservableProperty] public partial bool IsExpanded { get; set; }

    public OperationSectionViewModel(MainViewModel owner, OperationSection section, string header, IReadOnlyList<ActionChoice> operations)
    {
        this.owner = owner; Section = section; Header = header; this.operations = operations;
        owner.PropertyChanged += OnOwnerChanged;
    }

    private void OnOwnerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedOperation))
        {
            if (ContainsSelection) IsExpanded = true;
            OnPropertyChanged(nameof(SelectedOperation));
        }
        if (e.PropertyName == nameof(MainViewModel.FavouriteOperations) && Section == OperationSection.Favourites)
        {
            if (ContainsSelection) IsExpanded = true;
            OnPropertyChanged(nameof(Operations));
            OnPropertyChanged(nameof(IsVisible));
            OnPropertyChanged(nameof(SelectedOperation));
        }
        if (e.PropertyName == nameof(MainViewModel.RecipeChoices) && Section == OperationSection.Recipes)
        {
            OnPropertyChanged(nameof(Operations));
            OnPropertyChanged(nameof(IsVisible));
            OnPropertyChanged(nameof(SelectedOperation));
        }
    }

    partial void OnIsExpandedChanged(bool value)
    {
        // Keep the selected action visible; other groups can be freely collapsed.
        if (!value && ContainsSelection) { IsExpanded = true; return; }
        owner.SaveOperationSections();
    }
}
