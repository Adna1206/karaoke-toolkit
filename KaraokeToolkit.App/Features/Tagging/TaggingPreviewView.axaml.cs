using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace KaraokeToolkit.App.Features.Tagging;

public sealed partial class TaggingPreviewView : UserControl
{
    private TaggingViewModel Model => (TaggingViewModel)DataContext!;
    private readonly Func<Task<bool>>? confirmDiscard;
    private readonly TaskCompletionSource<TaggingRequest?> decision = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaggingPreviewView() => InitializeComponent();

    internal TaggingPreviewView(TaggingViewModel model, Func<Task<bool>> confirmDiscard) : this()
    {
        this.confirmDiscard = confirmDiscard;
        DataContext = model;
    }

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
        if (!Model.HasEdits || confirmDiscard != null && await confirmDiscard())
            decision.TrySetResult(null);
    }

    private void Apply_Click(object? sender, RoutedEventArgs args)
    {
        if (Model.BuildRequest() is { } request)
            decision.TrySetResult(request);
    }
}
