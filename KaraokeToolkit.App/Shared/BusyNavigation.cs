using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;

namespace KaraokeToolkit.App.Shared;

/// <summary>Shares the close and back guard for windows with background work.</summary>
internal sealed class BusyNavigation
{
    private readonly Window owner;
    private readonly string closeAction;
    private readonly Func<Action?>? getCancellation;
    private readonly Func<Task?>? getOperation;
    private bool pending;
    private bool leaving;

    internal BusyNavigation(Window owner, string closeAction = "Close",
        Func<Action?>? getCancellation = null, Func<Task?>? getOperation = null)
    {
        this.owner = owner;
        this.closeAction = closeAction;
        this.getCancellation = getCancellation;
        this.getOperation = getOperation;
        owner.Closing += OnClosing;
    }

    internal Task GoBackAsync(Action goBack) => NavigateAsync("Go back", goBack);

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (leaving || (!pending && owner.DataContext is not IBusyViewModel { IsBusy: true }))
            return;
        e.Cancel = true;
        if (!pending && e.CloseReason != WindowCloseReason.OSShutdown)
        {
            // Defer until the original close event has returned.
            Dispatcher.UIThread.Post(async () => await NavigateAsync(closeAction, owner.Close));
        }
    }

    private async Task NavigateAsync(string action, Action navigate)
    {
        if (pending || !owner.IsVisible)
            return;

        pending = true;
        try
        {
            if (owner.DataContext is IBusyViewModel { IsBusy: true } model)
            {
                var cancel = getCancellation?.Invoke();
                if (!await ConfirmAndWaitAsync(model,
                    () => BusyOperationDialog.ShowAsync(owner, action, cancel != null),
                    cancel, getOperation?.Invoke()))
                    return;
            }
            if (!owner.IsVisible)
                return;

            leaving = true;
            navigate();
        }
        finally
        {
            leaving = false;
            pending = false;
        }
    }

    internal static async Task<bool> ConfirmAndWaitAsync(IBusyViewModel model, Func<Task<bool>> confirm,
        Action? cancel, Task? operation)
    {
        if (!await confirm())
            return false;

        if (model.IsBusy)
            cancel?.Invoke();

        if (operation != null)
            await operation.ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext | ConfigureAwaitOptions.SuppressThrowing);

        if (model.IsBusy)
        {
            var idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnBusyChanged(object? sender, PropertyChangedEventArgs e)
            {
                if (e.PropertyName == nameof(IBusyViewModel.IsBusy) && !model.IsBusy)
                    idle.TrySetResult();
            }
            model.PropertyChanged += OnBusyChanged;
            try
            {
                if (model.IsBusy)
                    await idle.Task;
            }
            finally { model.PropertyChanged -= OnBusyChanged; }
        }
        return true;
    }
}
