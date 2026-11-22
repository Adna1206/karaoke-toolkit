using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using KaraokeToolkit.App.Features.Recipes.Dialogs;
using KaraokeToolkit.App.Shared;
using KaraokeToolkit.Core.Recipes;

namespace KaraokeToolkit.App.Features.Recipes;

public partial class RecipeBuilderWindow : Window
{
    public RecipeBuilderWindow()
    {
        InitializeComponent();
        Title = WindowTitles.For("Recipe Builder (Experimental)");
    }
    private RecipeBuilderViewModel Model => (RecipeBuilderViewModel)DataContext!;
    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
    private void OpenSavedRecipes_Click(object? sender, RoutedEventArgs e)
    {
        var menu = new MenuFlyout
        {
            Placement = PlacementMode.BottomEdgeAlignedLeft,
            ItemsSource = Model.SavedRecipes.Select(recipe =>
            {
                var item = new MenuItem { Header = recipe.Name };
                item.Click += (_, _) => Model.SelectedSavedRecipe = recipe;
                return item;
            }).ToArray()
        };
        menu.ShowAt(RecipeNameField);
    }

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        try { Model.Save(); }
        catch (Exception error) { Model.SetStatus("Could not save recipe: " + error.Message, StatusKind.Failure); }
    }
    private async void Delete_Click(object? sender, RoutedEventArgs e)
    {
        if (Model.SelectedSavedRecipe is not { } recipe) return;
        try
        {
            if (await DeleteRecipeDialog.ShowAsync(this, recipe.Name))
            {
                Model.DeleteSavedRecipe(recipe.Id);
            }
        }
        catch (Exception error) { Model.SetStatus("Could not delete recipe: " + error.Message, StatusKind.Failure); }
    }
    private static FilePickerFileType RecipeFile => new("Karaoke Toolkit recipe") { Patterns = ["*.json"] };
    private async void Import_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var files = await ModalDialog.RunAsync(this, () => StorageProvider.OpenFilePickerAsync(new() { Title = "Import Recipe", FileTypeFilter = [RecipeFile] }));
            if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
            Model.Load(RecipeJson.Deserialize(await File.ReadAllTextAsync(path)) with { Id = Guid.NewGuid() });
            Model.SelectedSavedRecipe = null;
            Model.SetStatus("Recipe imported. Save to keep it in your library.", StatusKind.Info);
        }
        catch (Exception error) { Model.SetStatus("Could not import recipe: " + error.Message, StatusKind.Failure); }
    }
    private async void Export_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            string json = RecipeJson.Serialize(Model.Definition());
            var file = await ModalDialog.RunAsync(this, () => StorageProvider.SaveFilePickerAsync(new() { Title = "Export Recipe", SuggestedFileName = "recipe.json", DefaultExtension = "json", FileTypeChoices = [RecipeFile] }));
            if (file?.TryGetLocalPath() is { } path) await File.WriteAllTextAsync(path, json);
        }
        catch (Exception error) { Model.SetStatus("Could not export recipe: " + error.Message, StatusKind.Failure); }
    }
    private async void Settings_Click(object? sender, RoutedEventArgs e)
    {
        if (Model.SelectedStep is not { } item) return;
        var window = new RecipeStepSettingsWindow(item.Step);
        await ModalDialog.ShowAsync(window, this);
        if (window.Result is { } step) Model.UpdateStep(item, step);
    }
}
