using KaraokeToolkit.App.Features.Main;
using KaraokeToolkit.Core.Conversion;

namespace KaraokeToolkit.Tests.App.Main;

/// <summary>Checks run-scoped cancellation and suppresses delayed progress after completion or replacement.</summary>
public class ProcessingRunTests
{
    /// <summary>Holds posted callbacks until explicitly drained to reproduce late UI progress deterministically.</summary>
    private sealed class QueuedContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> pending = new();
        public override void Post(SendOrPostCallback callback, object? state) => pending.Enqueue((callback, state));
        public void Drain()
        {
            while (pending.TryDequeue(out var work)) work.Callback(work.State);
        }
    }

    private static ProcessingRun CreateRun(QueuedContext context, Action<ConversionProgress> report,
        Action<string>? updateElapsed = null, TimeProvider? clock = null, Action<ProcessingEstimate?>? updateEstimate = null)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        try { return new ProcessingRun(report, updateElapsed, clock, updateEstimate); }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    [Fact]
    public void ElapsedTicksWithoutProgressAndFreezesWhenRunStops()
    {
        var context = new QueuedContext();
        var clock = new TestClock();
        var updates = new List<string>();
        using var run = CreateRun(context, _ => { }, updates.Add, clock);
        Assert.Equal("00:00:00", run.Elapsed);

        clock.Tick();
        Assert.Empty(updates);
        context.Drain();
        clock.Tick();
        context.Drain();
        Assert.Equal(new[] { "00:00:01", "00:00:02" }, updates);

        clock.Tick();
        run.StopProgress();
        context.Drain();
        clock.Tick();
        context.Drain();
        Assert.Equal("00:00:03", run.Elapsed);
        Assert.Equal("00:00:02", updates.Last());
    }

    [Fact]
    public void TimingEstimateUsesNewCompletionsAndRefreshesOnTheExistingTimer()
    {
        var context = new QueuedContext();
        var clock = new TestClock();
        var estimates = new List<ProcessingEstimate?>();
        using var run = CreateRun(context, _ => { }, clock: clock, updateEstimate: estimates.Add);

        Report(20);
        Tick();
        Report(21);
        Tick();
        Report(23);
        Assert.Empty(estimates);

        Tick();
        Assert.Equal(new ProcessingEstimate(77, 1), Assert.Single(estimates));
        Report(27);
        Tick();
        Tick();
        Assert.Single(estimates);
        Tick();
        Assert.Equal(7.0 / 6, estimates.Last()!.Value.ItemsPerSecond, 6);
        Assert.Equal(73 / (7.0 / 6), estimates.Last()!.Value.RemainingSeconds, 6);

        run.StopProgress();
        Assert.Null(estimates.Last());
        Tick();
        Assert.Equal(3, estimates.Count);

        void Report(int completed)
        {
            run.Progress.Report(new(0, 0, 100, completed, "Processing", ""));
            context.Drain();
        }

        void Tick() { clock.Tick(); context.Drain(); }
    }

    [Fact]
    public async Task PreparationAndOverwriteReviewClearTheEstimateAndRestartSampling()
    {
        var context = new QueuedContext();
        var clock = new TestClock();
        ProcessingEstimate? estimate = null;
        using var run = CreateRun(context, _ => { }, clock: clock, updateEstimate: value => estimate = value);

        Report(0, preparing: true);
        Tick(12);
        Report(3);
        Report(6);
        Tick(3);
        Assert.Equal(new ProcessingEstimate(94, 1), estimate);

        var decision = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var review = run.WithEstimatePausedAsync(() => decision.Task);
        Tick(20);
        Assert.Null(estimate);
        decision.SetResult(true);
        Assert.True(await review);

        Report(6);
        Report(9);
        Tick(3);
        Assert.Equal(new ProcessingEstimate(91, 1), estimate);

        Report(0, preparing: true);
        Assert.Null(estimate);
        Report(0);
        Report(3);
        Tick(3);
        Assert.Equal(new ProcessingEstimate(97, 1), estimate);
        Tick(9);
        Assert.Null(estimate);

        void Report(int completed, bool preparing = false)
        {
            run.Progress.Report(new(0, 0, 100, completed, "Processing", "") { IsIndeterminate = preparing });
            context.Drain();
        }

        void Tick(int seconds)
        {
            for (int i = 0; i < seconds; i++) { clock.Tick(); context.Drain(); }
        }
    }

    [Fact]
    public void SlowBatchesCanStillWarmUpAnEstimate()
    {
        var context = new QueuedContext();
        var clock = new TestClock();
        ProcessingEstimate? estimate = null;
        using var run = CreateRun(context, _ => { }, clock: clock, updateEstimate: value => estimate = value);
        run.Progress.Report(new(0, 0, 10, 0, "Processing", ""));
        context.Drain();

        for (int completed = 1; completed <= 3; completed++)
        {
            for (int second = 0; second < 11; second++) { clock.Tick(); context.Drain(); }
            run.Progress.Report(new(0, 0, 10, completed, "Processing", ""));
            context.Drain();
            clock.Tick();
            context.Drain();
        }

        Assert.Equal(1.0 / 12, estimate!.Value.ItemsPerSecond, 6);
        Assert.Equal(84, estimate.Value.RemainingSeconds, 6);
    }

    private sealed class TestClock : TimeProvider
    {
        private long timestamp;
        private TestTimer? timer;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => timestamp;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Assert.Equal(TimeSpan.FromSeconds(1), dueTime);
            Assert.Equal(dueTime, period);
            return timer = new TestTimer(() => callback(state));
        }

        public void Tick()
        {
            timestamp += TimeSpan.TicksPerSecond;
            timer!.Tick();
        }

        private sealed class TestTimer(Action callback) : ITimer
        {
            private bool disposed;
            public void Tick() { if (!disposed) callback(); }
            public bool Change(TimeSpan dueTime, TimeSpan period) => throw new NotSupportedException();
            public void Dispose() => disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void QueuedProgressCannotOverwriteCompletionOrTheNextRun(bool dispose)
    {
        var context = new QueuedContext();
        string status = "Working";
        using var first = CreateRun(context, progress => status = progress.Message);
        first.Progress.Report(new(10, 10, 1, 0, "First progress", "0"));
        context.Drain();
        Assert.Equal("First progress", status);

        first.Progress.Report(new(20, 20, 1, 0, "Stale progress", "0"));
        if (dispose) first.Dispose();
        else first.StopProgress();
        status = "Finished";
        context.Drain();
        Assert.Equal("Finished", status);

        using var second = CreateRun(context, progress => status = progress.Message);
        second.Progress.Report(new(5, 5, 1, 0, "New run", "0"));
        first.Progress.Report(new(30, 30, 1, 0, "Late first run", "0"));
        context.Drain();
        Assert.Equal("New run", status);
    }

    [Fact]
    public void CancellationBelongsToOneRunAndDisposalIsSafeAfterFailure()
    {
        var context = new QueuedContext();
        var failedRun = CreateRun(context, _ => throw new Exception("Must not accept queued progress after failure"));
        using var nextRun = CreateRun(context, _ => { });
        failedRun.Progress.Report(new(10, 10, 1, 0, "Queued before failure", "0"));

        failedRun.Cancel();
        Assert.True(failedRun.Token.IsCancellationRequested);
        Assert.False(nextRun.Token.IsCancellationRequested);

        failedRun.Dispose();
        failedRun.Dispose();
        failedRun.Cancel();
        context.Drain();
        Assert.False(nextRun.Token.IsCancellationRequested);
    }
}
