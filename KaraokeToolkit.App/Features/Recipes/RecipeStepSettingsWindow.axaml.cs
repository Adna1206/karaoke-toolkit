using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using KaraokeToolkit.App.Features.Settings;
using KaraokeToolkit.App.Shared;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Recipes;

namespace KaraokeToolkit.App.Features.Recipes;

public partial class RecipeStepSettingsWindow : Window
{
    private readonly RecipeStep step;
    public RecipeStep? Result { get; private set; }
    public RecipeStepSettingsWindow() : this(new() { OperationId = "ValidateKaraoke" }) { }
    public RecipeStepSettingsWindow(RecipeStep step)
    {
        this.step = step;
        InitializeComponent();
        Title = WindowTitles.For("Recipe Step Settings");
        DataContext = new RecipeStepOptionsViewModel(step);
    }
    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close();
    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        var model = (RecipeStepOptionsViewModel)DataContext!;
        try
        {
            var candidate = model.ToStep(step);
            RecipeOperations.Validate(new() { Steps = [candidate] });
            Result = candidate;
            Close();
        }
        catch (Exception error) { model.Error = error.Message; }
    }
}
