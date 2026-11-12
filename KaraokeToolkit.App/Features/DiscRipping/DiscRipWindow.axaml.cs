using KaraokeToolkit.App.Shared;
using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
namespace KaraokeToolkit.App.Features.DiscRipping;
public partial class DiscRipWindow : Window
{
    public DiscRipWindow()
    {
        InitializeComponent();
        Closing += (_, e) => { if (DataContext is DiscRipViewModel { IsBusy: true }) e.Cancel = true; };
        Opened += async (_, _) => { if (DataContext is DiscRipViewModel model) await model.RefreshCommand.ExecuteAsync(null); };
    }
    private async void Browse_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DiscRipViewModel model) return;
        try
        {
            var selected = await ModalDialog.RunAsync(this, () => StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose ripped-track output folder" }));
            if (selected.Count > 0 && selected[0].TryGetLocalPath() is string path) model.Destination = path;
        }
        catch (Exception error) { model.Status = error.Message; }
    }
    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
