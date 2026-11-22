using System.Threading.Tasks;
using Avalonia.Controls;
using KaraokeToolkit.App.Shared;

namespace KaraokeToolkit.App.Features.Main.Dialogs;

internal static class RestoreQuarantinedFilesDialog
{
    public static Task<bool> ShowAsync(Window owner, string manifestPath) =>
        PromptDialog.ConfirmAsync(owner, "Restore quarantined files?",
            "Files return to their original paths. Any existing files are kept.",
            "Restore files", details: manifestPath, width: 610);
}
