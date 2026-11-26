using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace KaraokeToolkit.App.Features.FilenameRename;

public sealed partial class FilenameRenamePreviewView : UserControl
{
    private FilenameRenameViewModel Model => (FilenameRenameViewModel)DataContext!;
    private readonly Func<bool, bool, Task<bool>>? confirmBack;
    private readonly TaskCompletionSource<FilenameRenameSubmission?> decision = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public FilenameRenamePreviewView() => InitializeComponent();

    internal FilenameRenamePreviewView(FilenameRenameViewModel model, Func<bool, bool, Task<bool>> confirmBack) : this()
    {
        this.confirmBack = confirmBack;
        DataContext = model;
    }

    internal void CancelPreview() => decision.TrySetResult(null);

    internal async Task<FilenameRenameSubmission?> ReviewAsync()
    {
        var preview = Model.RefreshAsync();
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
        if (confirmBack == null ? !Model.HasEdits : await confirmBack(Model.IsBusy, Model.HasEdits))
            CancelPreview();
    }

    private void Apply_Click(object? sender, RoutedEventArgs args)
    {
        if (Model.BuildSubmission() is { } submission)
            decision.TrySetResult(submission);
    }
}
