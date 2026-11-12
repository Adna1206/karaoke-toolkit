using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Input;
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
    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        try { Model.Save(); }
        catch (Exception error) { Model.Error = "Could not save recipe: " + error.Message; }
    }
    private async void Delete_Click(object? sender, RoutedEventArgs e)
    {
        if (Model.SelectedSavedRecipe is not { } recipe) return;
        try
        {
            if (await ConfirmDeleteAsync(recipe.Name)) Model.DeleteSavedRecipe(recipe.Id);
        }
        catch (Exception error) { Model.Error = "Could not delete recipe: " + error.Message; }
    }
    private async Task<bool> ConfirmDeleteAsync(string name)
    {
        var dialog = new Window
        {
            Title = WindowTitles.For("Delete Recipe"), Width = 470, CanResize = false,
            SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        bool confirmed = false;
        var cancel = new Button { Content = "Cancel" };
        var delete = new Button { Content = "Delete Recipe" };
        cancel.Click += (_, _) => dialog.Close();
        delete.Click += (_, _) => { confirmed = true; dialog.Close(); };
        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(24), Spacing = 18,
            Children =
            {
                new TextBlock { Text = $"Delete “{name}” from your saved recipes?\n\nUnsaved edits to this recipe will be discarded. Karaoke files and exported recipe copies will not be deleted.", TextWrapping = TextWrapping.Wrap },
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 10, Children = { cancel, delete } }
            }
        };
        dialog.Opened += (_, _) => cancel.Focus();
        dialog.KeyDown += (_, key) => { if (key.Key == Key.Escape) { key.Handled = true; dialog.Close(); } };
        await ModalDialog.ShowAsync(dialog, this);
        return confirmed;
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
            Model.Error = "Recipe imported. Save to keep it in your library.";
        }
        catch (Exception error) { Model.Error = "Could not import recipe: " + error.Message; }
    }
    private async void Export_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            string json = RecipeJson.Serialize(Model.Definition());
            var file = await ModalDialog.RunAsync(this, () => StorageProvider.SaveFilePickerAsync(new() { Title = "Export Recipe", SuggestedFileName = "recipe.json", DefaultExtension = "json", FileTypeChoices = [RecipeFile] }));
            if (file?.TryGetLocalPath() is { } path) await File.WriteAllTextAsync(path, json);
        }
        catch (Exception error) { Model.Error = "Could not export recipe: " + error.Message; }
    }
    private async void Settings_Click(object? sender, RoutedEventArgs e)
    {
        if (Model.SelectedStep is not { } item) return;
        var window = new RecipeStepSettingsWindow(item.Step);
        await ModalDialog.ShowAsync(window, this);
        if (window.Result is { } step) Model.UpdateStep(item, step);
    }
}
