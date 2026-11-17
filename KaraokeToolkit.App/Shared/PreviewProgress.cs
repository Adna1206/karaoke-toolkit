using System;
using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace KaraokeToolkit.App.Shared;

public sealed partial class PreviewProgress : ObservableObject
{
    [ObservableProperty] public partial bool IsVisible { get; private set; }
    [ObservableProperty] public partial bool IsIndeterminate { get; private set; }
    [ObservableProperty] public partial double Percent { get; private set; }

    private object? current;

    internal Action<string, int, int> Begin(Action<string> setStatus)
    {
        var preview = new object();
        current = preview;
        Percent = 0;
        IsIndeterminate = true;
        IsVisible = true;
        string? lastStage = null;
        long lastUpdate = 0;

        return (stage, completed, total) =>
        {
            if (stage == lastStage && Stopwatch.GetElapsedTime(lastUpdate).TotalMilliseconds < 100)
                return;
            lastStage = stage;
            lastUpdate = Stopwatch.GetTimestamp();

            Dispatcher.UIThread.Post(() =>
            {
                if (!ReferenceEquals(current, preview))
                    return;
                Percent = total > 0 ? 100.0 * completed / total : 0;
                IsIndeterminate = total <= 0;
                setStatus(total > 0 ? $"{stage} · {completed:N0} of {total:N0}" : stage + "…");
            });
        };
    }

    internal void Finish()
    {
        current = null;
        IsVisible = false;
    }
}
