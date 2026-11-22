using System.Threading.Tasks;
using Avalonia.Controls;
using KaraokeToolkit.App.Shared;

namespace KaraokeToolkit.App.Features.Reports.Dialogs;

internal static class MoveFilesToDamagedDialog
{
    public static Task<bool> ShowAsync(Window owner, string files) =>
        PromptDialog.ConfirmAsync(owner, "Move files to Damaged?",
            "Matching files move together into a new batch under Damaged. Use its manifest to restore them.",
            "Move files", details: files, width: 610);
}
