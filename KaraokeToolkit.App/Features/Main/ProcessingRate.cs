using System;
using KaraokeToolkit.Core.Conversion;

namespace KaraokeToolkit.App.Features.Main;

internal readonly record struct ProcessingEstimate(double RemainingSeconds, double ItemsPerSecond);

/// <summary>Estimates the current batch from changes in its completed count and a monotonic clock.</summary>
internal sealed class ProcessingRate(TimeProvider clock)
{
    private static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(3);
    private const double MinimumStaleSeconds = 9;
    private int total, completed, sampledCompleted;
    private long sampledAt, lastCompletionAt;
    private double? rate;

    internal ProcessingEstimate? Estimate { get; private set; }

    internal void Observe(ConversionProgress progress)
    {
        if (progress.IsIndeterminate || progress.Completed >= progress.Found)
        {
            Reset();
            return;
        }

        long now = clock.GetTimestamp();
        if (progress.Found != total || progress.Completed < completed)
        {
            Reset();
            total = progress.Found;
            sampledCompleted = progress.Completed;
            sampledAt = lastCompletionAt = now;
        }
        else if (progress.Completed > completed)
            lastCompletionAt = now;

        completed = progress.Completed;
    }

    internal void Sample()
    {
        if (total == 0) return;
        long now = clock.GetTimestamp();
        if (rate is double current && clock.GetElapsedTime(lastCompletionAt, now).TotalSeconds >= Math.Max(MinimumStaleSeconds, 3 / current))
            Estimate = null;

        TimeSpan elapsed = clock.GetElapsedTime(sampledAt, now);
        int advanced = completed - sampledCompleted;
        if (elapsed < SampleInterval || advanced < (rate == null ? 3 : 1)) return;

        double recentRate = advanced / elapsed.TotalSeconds;
        rate = rate is double previous ? (previous + recentRate) / 2 : recentRate;
        Estimate = new((total - completed) / rate.Value, rate.Value);
        sampledAt = now;
        sampledCompleted = completed;
    }

    internal void Reset()
    {
        total = 0;
        Estimate = null;
        rate = null;
    }
}
