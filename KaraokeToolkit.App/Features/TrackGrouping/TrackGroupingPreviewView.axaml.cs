using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using KaraokeToolkit.App.Shared;
using System;
using System.Threading.Tasks;

namespace KaraokeToolkit.App.Features.TrackGrouping;

public sealed partial class TrackGroupingPreviewView : UserControl
{
    private TrackGroupingViewModel Model => (TrackGroupingViewModel)DataContext!;
    private readonly Func<Task<bool>>? confirmDiscard;
    private readonly TaskCompletionSource<TrackGroupingSubmission?> decision = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TrackGroupingPreviewView() => InitializeComponent();

    internal TrackGroupingPreviewView(TrackGroupingViewModel model, Func<Task<bool>> confirmDiscard) : this()
    {
        this.confirmDiscard = confirmDiscard;
        DataContext = model;
    }

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

    private async void Browse_Click(object? sender, RoutedEventArgs args)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        try
        {
            var folders = await ModalDialog.RunAsync(owner, () => owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Choose the grouping destination"
            }));
            foreach (var folder in folders)
            {
                using (folder)
                    if (folder.TryGetLocalPath() is { } path) Model.OutputFolder = path;
            }
        }
        catch (Exception error) { Model.StatusSeverity = StatusKind.Failure; Model.Status = "Could not select a destination folder. " + error.Message; }
    }

    private async void Back_Click(object? sender, RoutedEventArgs args)
    {
        if (!Model.HasEdits || confirmDiscard != null && await confirmDiscard())
            decision.TrySetResult(null);
    }

    private async void Refresh_Click(object? sender, RoutedEventArgs args)
    {
        await Model.PreviewAsync();
    }

    private void Apply_Click(object? sender, RoutedEventArgs args)
    {
        if (Model.GetSubmission() is { } submission)
            decision.TrySetResult(submission);
    }
}
