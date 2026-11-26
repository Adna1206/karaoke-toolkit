using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace KaraokeToolkit.App.Features.Tagging;

public sealed partial class TaggingPreviewView : UserControl
{
    private TaggingViewModel Model => (TaggingViewModel)DataContext!;
    private readonly Func<bool, bool, Task<bool>>? confirmBack;
    private readonly TaskCompletionSource<TaggingRequest?> decision = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaggingPreviewView() => InitializeComponent();

    internal TaggingPreviewView(TaggingViewModel model, Func<bool, bool, Task<bool>> confirmBack) : this()
    {
        this.confirmBack = confirmBack;
        DataContext = model;
    }

    internal void CancelPreview() => decision.TrySetResult(null);

    internal async Task<TaggingRequest?> ReviewAsync()
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

    private void Apply_Click(object? sender, RoutedEventArgs args)
    {
        if (Model.BuildRequest() is { } request)
            decision.TrySetResult(request);
    }
}
