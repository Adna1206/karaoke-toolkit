using System.Threading.Tasks;
using Avalonia.Controls;
using KaraokeToolkit.App.Shared;
using KaraokeToolkit.Core.Recovery;

namespace KaraokeToolkit.App.Features.Main.Dialogs;

internal static class InterruptedRunDialog
{
    public static async Task<MainViewModel.RecoveryChoice> ShowAsync(Window owner, RecoveryJob job)
    {
        var choice = MainViewModel.RecoveryChoice.Later;
        var recover = new Button { Content = job.ContinueBatchAfterRecovery ? "Recover and resume" : "Recover interrupted item",
            Classes = { "accent" }, IsEnabled = job.Problem == null };
        var later = new Button { Content = "Later" };
        var abandon = new Button { Content = "Abandon interrupted run…", Classes = { "destructive" } };
        var units = job.Kind switch
        {
            RecoveryKind.Recipe => "recipe steps",
            RecoveryKind.Tagging or RecoveryKind.AudioGain => "files",
            _ => "source groups"
        };
        var body = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                PromptDialog.Message($"{job.Action} · {job.Completed} of {job.Total} {units} recorded."),
                PromptDialog.Message(job.Folder),
                PromptDialog.Message(job.Problem ?? (job.ContinueBatchAfterRecovery
                    ? "Recovery checks the saved file state before resuming. Completed work is kept."
                    : "Recovery handles the interrupted item if there is one. You can start a new run for the remaining items."))
            }
        };
        var dialog = PromptDialog.Create("Interrupted run found", body, 610, DialogArtwork.Recovery, false,
            later, abandon, recover);
        recover.Click += (_, _) => { choice = MainViewModel.RecoveryChoice.Resume; dialog.Close(); };
        later.Click += (_, _) => dialog.Close();
        abandon.Click += async (_, _) =>
        {
            if (await AbandonInterruptedRunDialog.ShowAsync(dialog, job.Folder))
            {
                choice = MainViewModel.RecoveryChoice.Abandon;
                dialog.Close();
            }
        };
        dialog.Opened += (_, _) => later.Focus();
        await ModalDialog.ShowAsync(dialog, owner);
        return choice;
    }
}
