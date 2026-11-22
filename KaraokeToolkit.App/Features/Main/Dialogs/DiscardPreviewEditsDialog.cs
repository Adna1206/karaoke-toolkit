using System.Threading.Tasks;
using Avalonia.Controls;
using KaraokeToolkit.App.Shared;

namespace KaraokeToolkit.App.Features.Main.Dialogs;

internal static class DiscardPreviewEditsDialog
{
    public static Task<bool> ShowAsync(Window owner) =>
        PromptDialog.ConfirmAsync(owner, "Discard preview edits?",
            "You’ll return to the operation list and lose your edits to this preview.", "Discard edits", destructive: true);
}
