using System;
using System.Threading;
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
        using var cancellation = new CancellationTokenSource();
        var preview = Model.PreviewAsync(cancellation.Token);
        try
        {
            if (await Task.WhenAny(preview, decision.Task) == preview)
                await preview;
            return await decision.Task;
        }
        finally
        {
            cancellation.Cancel();
            Model.PreviewCommand.Cancel();
        }
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
