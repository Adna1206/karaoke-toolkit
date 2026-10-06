using System.Threading;
using Avalonia.Controls;
using Avalonia.Interactivity;
using KaraokeToolkit.App.Shared;

namespace KaraokeToolkit.App.Features.Tagging;

public partial class TaggingWindow : Window
{
    private readonly CancellationTokenSource previewCancellation = new();
    public TaggingWindow()
    {
        InitializeComponent();
        Opened += async (_, _) => { if (DataContext is TaggingViewModel model) await model.PreviewAsync(previewCancellation.Token); };
        Closed += (_, _) => previewCancellation.Cancel();
    }
    private void Cancel_Click(object? sender, RoutedEventArgs args) => Close();
    private void Apply_Click(object? sender, RoutedEventArgs args)
    {
        if (DataContext is TaggingViewModel model && model.BuildRequest() is { } request) Close(request);
    }
}
