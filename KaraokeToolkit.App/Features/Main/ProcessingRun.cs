using System;
using System.Threading;
using KaraokeToolkit.Core.Conversion;

namespace KaraokeToolkit.App.Features.Main;

/// <summary>Owns one run's cancellation and UI-dispatched progress lifetime.</summary>
internal sealed class ProcessingRun : IDisposable
{
    private readonly CancellationTokenSource cancellation = new();
    private readonly object gate = new();
    private bool accepting = true;
    private bool disposed;

    public CancellationToken Token { get; }
    public IProgress<ConversionProgress> Progress { get; }

    public ProcessingRun(Action<ConversionProgress> report)
    {
        Token = cancellation.Token;
        // Capture the caller's UI context. Check lifetime when dispatched, not
        // when queued, so late callbacks cannot overwrite the completed report.
        Progress = new Progress<ConversionProgress>(value =>
        {
            lock (gate)
            {
                if (accepting) report(value);
            }
        });
    }

    public void StopProgress()
    {
        lock (gate) accepting = false;
    }

    public void Cancel()
    {
        lock (gate)
        {
            if (!disposed) cancellation.Cancel();
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            accepting = false;
            disposed = true;
            cancellation.Dispose();
        }
    }
}
