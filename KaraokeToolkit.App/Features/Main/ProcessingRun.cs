using System;
using System.Threading;
using KaraokeToolkit.Core.Conversion;

namespace KaraokeToolkit.App.Features.Main;

/// <summary>Owns one run's cancellation, elapsed clock and UI-dispatched progress.</summary>
internal sealed class ProcessingRun : IDisposable
{
    private readonly CancellationTokenSource cancellation = new();
    private readonly object gate = new();
    private readonly TimeProvider clock;
    private readonly long startedAt;
    private readonly ITimer? elapsedTimer;
    private long? stoppedAt;
    private bool accepting = true;
    private bool disposed;

    public CancellationToken Token { get; }
    public IProgress<ConversionProgress> Progress { get; }
    public string Elapsed
    {
        get
        {
            lock (gate)
            {
                TimeSpan elapsed = clock.GetElapsedTime(startedAt, stoppedAt ?? clock.GetTimestamp());
                return FormattableString.Invariant($"{(long)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}");
            }
        }
    }

    public ProcessingRun(Action<ConversionProgress> report, Action<string>? updateElapsed = null, TimeProvider? clock = null)
    {
        Token = cancellation.Token;
        this.clock = clock ?? TimeProvider.System;
        startedAt = this.clock.GetTimestamp();
        // Capture the caller's UI context. Check lifetime when dispatched, not
        // when queued, so late callbacks cannot overwrite the completed report.
        Progress = new Progress<ConversionProgress>(value =>
        {
            lock (gate)
            {
                if (accepting) report(value);
            }
        });
        if (updateElapsed != null)
        {
            var context = SynchronizationContext.Current ?? new SynchronizationContext();
            TimeSpan interval = TimeSpan.FromSeconds(1);
            elapsedTimer = this.clock.CreateTimer(_ => context.Post(UpdateElapsed, null), null, interval, interval);

            void UpdateElapsed(object? state)
            {
                lock (gate)
                {
                    if (accepting) updateElapsed(Elapsed);
                }
            }
        }
    }

    /// <summary>Freezes elapsed time and rejects queued updates without requesting cancellation.</summary>
    public void StopProgress()
    {
        lock (gate)
        {
            if (!accepting) return;
            stoppedAt = clock.GetTimestamp();
            accepting = false;
            elapsedTimer?.Dispose();
        }
    }

    public void Cancel()
    {
        lock (gate)
        {
            if (!disposed) cancellation.Cancel();
        }
    }

    /// <summary>Stops progress and releases the cancellation source without cancelling the operation.</summary>
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            StopProgress();
            disposed = true;
            cancellation.Dispose();
        }
    }
}
