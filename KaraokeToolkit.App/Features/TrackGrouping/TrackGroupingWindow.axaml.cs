using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using KaraokeToolkit.App.Shared;
using System;

namespace KaraokeToolkit.App.Features.TrackGrouping;

public sealed partial class TrackGroupingWindow : Window
{
    public TrackGroupingWindow()
    {
        InitializeComponent();
        Title = WindowTitles.For("Group by Track ID");
        Opened += async (_, _) =>
        {
            if (DataContext is TrackGroupingViewModel model) await model.PreviewAsync();
        };
        Closed += (_, _) => (DataContext as TrackGroupingViewModel)?.StopPreview();
    }

    private async void Browse_Click(object? sender, RoutedEventArgs args)
    {
        if (DataContext is not TrackGroupingViewModel model) return;
        try
        {
            var folders = await ModalDialog.RunAsync(this, () => StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Choose the grouping destination"
            }));
            foreach (var folder in folders)
            {
                using (folder)
                    if (folder.TryGetLocalPath() is { } path) model.OutputFolder = path;
            }
        }
        catch (Exception error) { model.Status = "Could not select a destination folder. " + error.Message; }
    }

    private void Cancel_Click(object? sender, RoutedEventArgs args) => Close();

    private async void Refresh_Click(object? sender, RoutedEventArgs args)
    {
        if (DataContext is TrackGroupingViewModel model) await model.PreviewAsync();
    }

    private void Apply_Click(object? sender, RoutedEventArgs args)
    {
        if (DataContext is TrackGroupingViewModel model && model.GetSubmission() is { } submission)
            Close(submission);
    }
}
