using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using KaraokeToolkit.App.Services;
using KaraokeToolkit.App.Shared;

namespace KaraokeToolkit.App.Features.Main.Dialogs;

internal static class SoftwareUpdatesDialog
{
    public static async Task CheckAsync(Window owner, bool manual, Func<bool> isBusy)
    {
        var status = PromptDialog.Message("Looking for a newer version…");
        var progress = new ProgressBar { IsIndeterminate = true };
        var updateButton = new Button { Content = "Update", Classes = { "accent" }, MinWidth = 85, IsVisible = false };
        var close = new Button { Content = "Close", MinWidth = 85 };
        var dialog = PromptDialog.Create("Software updates", CreateUpdateStatus(status, progress), 480,
            DialogArtwork.Update, false, close, updateButton);
        close.Click += (_, _) => dialog.Close();
        dialog.Opened += (_, _) => close.Focus();
        using var downloadCancellation = new CancellationTokenSource();
        dialog.Closed += (_, _) => downloadCancellation.Cancel();
        Task? dialogClosed = manual ? ModalDialog.ShowAsync(dialog, owner) : null;
        Task? download = null;
        try
        {
            var (manager, isInstalled, update) = await Task.Run(UpdateService.CheckForUpdatesAsync);
            if (downloadCancellation.IsCancellationRequested || !owner.IsVisible ||
                (!manual && (update == null || isBusy())))
                return;

            status.Text = (isInstalled, update) switch
            {
                (false, _) => "Install Karaoke Toolkit with Velopack to check for updates.",
                (true, null) => "No newer version is available.",
                (true, { } available) => $"Version {available.TargetFullRelease.Version} is available."
            };
            if (update != null)
            {
                bool ready = manager.UpdatePendingRestart?.Version == update.TargetFullRelease.Version;
                void ShowReady()
                {
                    status.Text = "Update downloaded. Restart Karaoke Toolkit to install it.";
                    updateButton.Content = "Restart to update";
                }

                if (ready) ShowReady();
                updateButton.IsVisible = true;
                updateButton.Click += async (_, _) =>
                {
                    if (isBusy())
                    {
                        status.Text = "Wait for the current operation to finish before updating.";
                        return;
                    }

                    if (ready)
                    {
                        try { manager.ApplyUpdatesAndRestart(update.TargetFullRelease, []); }
                        catch (Exception error)
                        {
                            Trace.TraceWarning("Could not apply update: {0}", error);
                            status.Text = "Could not restart to install the update. Try again.";
                        }
                        return;
                    }

                    if (download is { IsCompleted: false }) return;
                    download = DownloadAsync();
                    await download;
                };

                async Task DownloadAsync()
                {
                    updateButton.IsEnabled = false;
                    status.Text = $"Downloading version {update.TargetFullRelease.Version}…";
                    progress.IsIndeterminate = false;
                    progress.Value = 0;
                    progress.IsVisible = true;
                    IProgress<int> report = new Progress<int>(percent =>
                    {
                        if (progress.IsVisible && !downloadCancellation.IsCancellationRequested)
                            progress.Value = percent;
                    });
                    try
                    {
                        await Task.Run(() => manager.DownloadUpdatesAsync(update, report.Report, downloadCancellation.Token));
                        if (downloadCancellation.IsCancellationRequested) return;
                        ready = true;
                        ShowReady();
                    }
                    catch (OperationCanceledException) when (downloadCancellation.IsCancellationRequested) { }
                    catch (Exception error)
                    {
                        Trace.TraceWarning("Could not download update: {0}", error);
                        status.Text = "Could not download the update. Try again.";
                    }
                    finally
                    {
                        progress.IsVisible = false;
                        updateButton.IsEnabled = true;
                    }
                }
            }
        }
        catch (Exception error)
        {
            Trace.TraceWarning("GitHub Releases update check failed: {0}", error);
            if (!manual) return;
            status.Text = "Could not check for updates. Try again later.";
        }

        progress.IsVisible = false;
        await (dialogClosed ?? ModalDialog.ShowAsync(dialog, owner));
        if (download != null) await download;
    }

    private static Grid CreateUpdateStatus(TextBlock message, ProgressBar progress)
    {
        message.VerticalAlignment = VerticalAlignment.Center;
        progress.Width = WindowSpacing.StatusProgressWidth;
        progress.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(progress, 1);
        return new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = WindowSpacing.StatusProgressGap,
            Children = { message, progress }
        };
    }
}
