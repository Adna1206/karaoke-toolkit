using System.Threading.Tasks;
using Avalonia.Controls;
using KaraokeToolkit.App.Shared;

namespace KaraokeToolkit.App.Features.Settings.Dialogs;

internal static class ResetSettingsDialog
{
    public static Task<bool> ShowAsync(Window owner) =>
        PromptDialog.ConfirmAsync(owner, "Reset settings to defaults?",
            "This resets your settings, including encoder bitrates and the custom cdrdao path. Select Apply or OK in Settings to save the reset.",
            "Reset to Defaults", destructive: true);
}
