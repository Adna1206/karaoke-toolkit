using System.Threading.Tasks;
using Avalonia.Controls;
using KaraokeToolkit.App.Shared;

namespace KaraokeToolkit.App.Features.Main.Dialogs;

internal static class OperationHelpDialog
{
    public static async Task ShowAsync(Window owner)
    {
        var body = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                PromptDialog.Message("Choose an operation, add files or folders, then select Start. Disc ripping and restoration ask for inputs after Start."),
                PromptDialog.Message("Use Tools → Settings to change processing options. After a run, open View Last Run Report to review the results.")
            }
        };
        var close = new Button { Content = "Got it", MinWidth = 85 };
        var dialog = PromptDialog.Create("Run an operation", body, 480, DialogArtwork.Help, false, close);
        close.Click += (_, _) => dialog.Close();
        dialog.Opened += (_, _) => close.Focus();
        await ModalDialog.ShowAsync(dialog, owner);
    }
}
