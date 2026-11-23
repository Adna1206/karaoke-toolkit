using Avalonia.Controls;
using Avalonia.Interactivity;
using System;
using System.Threading.Tasks;

namespace KaraokeToolkit.App.Features.TrackGrouping;

public sealed partial class TrackGroupingPreviewView : UserControl
{
    private TrackGroupingViewModel Model => (TrackGroupingViewModel)DataContext!;
    private readonly Func<bool, bool, Task<bool>>? confirmBack;
    private readonly TaskCompletionSource<TrackGroupingSubmission?> decision = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TrackGroupingPreviewView() => InitializeComponent();

    internal TrackGroupingPreviewView(TrackGroupingViewModel model, Func<bool, bool, Task<bool>> confirmBack) : this()
    {
        this.confirmBack = confirmBack;
        DataContext = model;
    }

    internal void CancelPreview() => decision.TrySetResult(null);

    internal async Task<TrackGroupingSubmission?> ReviewAsync()
    {
        var preview = Model.PreviewAsync();
        try
        {
            if (await Task.WhenAny(preview, decision.Task) == preview)
                await preview;
            return await decision.Task;
        }
        finally { Model.StopPreview(); }
    }

    private async void Back_Click(object? sender, RoutedEventArgs args)
    {
        if (confirmBack == null ? !Model.HasEdits : await confirmBack(Model.PreviewProgress.IsVisible, Model.HasEdits))
            CancelPreview();
    }

    private async void Refresh_Click(object? sender, RoutedEventArgs args)
    {
        await Model.PreviewAsync(refresh: true);
    }

    private void Apply_Click(object? sender, RoutedEventArgs args)
    {
        if (Model.GetSubmission() is { } submission)
            decision.TrySetResult(submission);
    }
}
