using System.Threading.Tasks;
using Avalonia.Controls;
using KaraokeToolkit.App.Shared;

namespace KaraokeToolkit.App.Features.Main.Dialogs;

internal static class AbandonInterruptedRunDialog
{
    public static Task<bool> ShowAsync(Window owner, string folder) =>
        PromptDialog.ConfirmAsync(owner, "Abandon interrupted run?",
            "The app will stop offering to resume this run. Completed changes and recovery files are kept. A pending transaction may still block changes in this folder.",
            "Abandon run", destructive: true, details: folder, width: 610);
}
