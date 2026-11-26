using System;
using System.Threading;
using System.Threading.Tasks;

namespace KaraokeToolkit.App.Shared;

/// <summary>Coordinates preview requests on the UI thread. Edits replace the plan and reuse the input scan.</summary>
internal sealed class PreviewWork<TSnapshot>
{
    private CancellationTokenSource? current;
    private CancellationTokenSource? scanCancellation;
    private Task<TSnapshot>? scan;
    private bool closed;

    internal bool HasStarted { get; private set; }
    internal bool CanUpdate => HasStarted && !closed;
    internal static TimeSpan EditDelay { get; } = TimeSpan.FromMilliseconds(225);

    internal CancellationTokenSource? Begin(bool refresh, CancellationToken token)
    {
        if (closed) return null;
        current?.Cancel();
        if (refresh) ResetScan();
        HasStarted = true;
        current = CancellationTokenSource.CreateLinkedTokenSource(token);
        return current;
    }

    internal Task<TSnapshot> ReadAsync(Func<CancellationToken, Task<TSnapshot>> read, CancellationToken token)
    {
        if (scan == null || scan.IsFaulted || scan.IsCanceled)
        {
            ResetScan();
            scanCancellation = new();
            scan = read(scanCancellation.Token);
            // The scan can outlive an edit waiting for it. Observe failures even if that edit goes away.
            _ = ObserveAsync(scan);
        }
        return scan.WaitAsync(token);
    }

    internal bool Finish(CancellationTokenSource request)
    {
        if (!ReferenceEquals(current, request)) return false;
        current = null;
        return true;
    }

    internal void Stop()
    {
        closed = true;
        current?.Cancel();
        current = null;
        ResetScan();
    }

    private void ResetScan()
    {
        scanCancellation?.Cancel();
        scanCancellation?.Dispose();
        scanCancellation = null;
        scan = null;
    }

    private static async Task ObserveAsync(Task task) =>
        await task.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
}
