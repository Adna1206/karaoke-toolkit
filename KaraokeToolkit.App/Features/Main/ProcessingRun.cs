using System;
using System.Threading;
using System.Threading.Tasks;
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
    private readonly ProcessingRate? processingRate;
    private readonly Action<ProcessingEstimate?>? updateEstimate;
    private ProcessingEstimate? displayedEstimate;
    private int estimatePauses;
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
                return FormatDuration(elapsed);
            }
        }
    }

    internal static string FormatDuration(TimeSpan duration) =>
        FormattableString.Invariant($"{(long)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}");

    public ProcessingRun(Action<ConversionProgress> report, Action<string>? updateElapsed = null, TimeProvider? clock = null,
        Action<ProcessingEstimate?>? updateEstimate = null)
    {
        Token = cancellation.Token;
        this.clock = clock ?? TimeProvider.System;
        startedAt = this.clock.GetTimestamp();
        this.updateEstimate = updateEstimate;
        if (updateEstimate != null) processingRate = new(this.clock);
        // Capture the caller's UI context. Check lifetime when dispatched, not
        // when queued, so late callbacks cannot overwrite the completed report.
        Progress = new Progress<ConversionProgress>(value =>
        {
            lock (gate)
            {
                if (!accepting) return;
                if (estimatePauses == 0 && !Token.IsCancellationRequested) processingRate?.Observe(value);
                ReportEstimate();
                report(value);
            }
        });
        if (updateElapsed != null || updateEstimate != null)
        {
            var context = SynchronizationContext.Current ?? new SynchronizationContext();
            TimeSpan interval = TimeSpan.FromSeconds(1);
            elapsedTimer = this.clock.CreateTimer(_ => context.Post(UpdateElapsed, null), null, interval, interval);

            void UpdateElapsed(object? state)
            {
                lock (gate)
                {
                    if (!accepting) return;
                    updateElapsed?.Invoke(Elapsed);
                    if (estimatePauses == 0 && !Token.IsCancellationRequested) processingRate?.Sample();
                    ReportEstimate();
                }
            }
        }
    }

    private void ReportEstimate()
    {
        var estimate = processingRate?.Estimate;
        if (estimate == displayedEstimate) return;
        displayedEstimate = estimate;
        updateEstimate?.Invoke(estimate);
    }

    internal async Task<T> WithEstimatePausedAsync<T>(Func<Task<T>> action)
    {
        // Conflict callbacks can start on a worker. The next UI timer tick
        // clears the display and samples start fresh when processing resumes.
        lock (gate)
        {
            estimatePauses++;
            processingRate?.Reset();
        }
        try { return await action(); }
        finally { lock (gate) estimatePauses--; }
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
            processingRate?.Reset();
            ReportEstimate();
        }
    }

    public void Cancel()
    {
        lock (gate)
        {
            if (disposed) return;
            cancellation.Cancel();
            processingRate?.Reset();
            ReportEstimate();
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
