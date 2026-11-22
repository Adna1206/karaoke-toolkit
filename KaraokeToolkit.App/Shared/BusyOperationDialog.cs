using System.Threading.Tasks;
using Avalonia.Controls;

namespace KaraokeToolkit.App.Shared;

internal static class BusyOperationDialog
{
    internal static async Task<bool> ShowAsync(Window owner, string action, bool canCancel, bool isPreview = false)
    {
        var stay = new Button { Content = "Keep working" };
        var leave = new Button
        {
            Content = canCancel ? $"Cancel and {action.ToLowerInvariant()}" : $"{action} when finished",
            Classes = { canCancel ? "destructive" : "accent" }
        };
        string message = isPreview
            ? "The preview will stop and any preview edits will be discarded."
            : canCancel
                ? "The current operation will stop at a safe point. Completed changes are kept."
                : action == "Go back"
                    ? "The current operation will finish before you return to the operation list."
                    : "The current operation will finish before this window closes.";
        var dialog = PromptDialog.Create($"{action} while an operation is running?", PromptDialog.Message(message), 480,
            DialogArtwork.Warning, canCancel, stay, leave);
        stay.Click += (_, _) => dialog.Close(false);
        leave.Click += (_, _) => dialog.Close(true);
        dialog.Opened += (_, _) => stay.Focus();
        return await ModalDialog.ShowAsync<bool?>(dialog, owner) == true;
    }
}
