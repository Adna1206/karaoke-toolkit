using System.ComponentModel;
using KaraokeToolkit.App.Shared;

namespace KaraokeToolkit.Tests.App.Shared;

public class BusyNavigationTests
{
    [Fact]
    public async Task ConfirmingRequestsCancellationAndWaitsForIdle()
    {
        var model = new BusyModel();
        var confirm = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var leave = BusyNavigation.ConfirmAndWaitAsync(model, () => confirm.Task, () => cancelled.TrySetResult(), null);

        Assert.False(cancelled.Task.IsCompleted);
        confirm.SetResult(true);
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(leave.IsCompleted);
        model.Finish();

        Assert.True(await leave.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task KeepingTheWindowOpenLeavesTheOperationRunning()
    {
        var model = new BusyModel();
        var leave = await BusyNavigation.ConfirmAndWaitAsync(model, () => Task.FromResult(false),
            () => throw new InvalidOperationException("The operation should continue."), null);

        Assert.False(leave);
        Assert.True(model.IsBusy);
    }

    [Fact]
    public async Task ConfirmationCanWaitForAnOperationThatCannotBeCancelled()
    {
        var model = new BusyModel();
        var operation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var leave = BusyNavigation.ConfirmAndWaitAsync(model, () => Task.FromResult(true), null, operation.Task);

        model.Finish();
        Assert.False(leave.IsCompleted);
        operation.SetResult();
        Assert.True(await leave.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task ConfirmingAfterNaturalCompletionDoesNotCancel()
    {
        var model = new BusyModel();
        var confirm = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var leave = BusyNavigation.ConfirmAndWaitAsync(model, () => confirm.Task,
            () => throw new InvalidOperationException("The operation has already finished."), null);

        model.Finish();
        confirm.SetResult(true);
        Assert.True(await leave.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    private sealed class BusyModel : IBusyViewModel
    {
        public bool IsBusy { get; private set; } = true;
        public event PropertyChangedEventHandler? PropertyChanged;

        public void Finish()
        {
            IsBusy = false;
            PropertyChanged?.Invoke(this, new(nameof(IsBusy)));
        }
    }
}
