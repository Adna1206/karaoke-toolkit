using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using KaraokeToolkit.App.Shared;
using KaraokeToolkit.Core.Conversion;

namespace KaraokeToolkit.App.Features.Main.Dialogs;

internal static class OutputConflictDialog
{
    public static async Task<ConflictDecision> ShowAsync(Window owner, OutputConflict conflict, CancellationToken token)
    {
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            token.ThrowIfCancellationRequested();
            var decision = ConflictDecision.Cancel;
            var cancel = new Button { Content = "Cancel operation" };
            var skip = new Button { Content = "Skip" };
            var replace = new Button { Content = "Replace files", Classes = { "destructive" } };
            var body = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    PromptDialog.Message("Skip keeps the existing output and its source. Replace overwrites this output group."),
                    new ScrollViewer
                    {
                        MaxHeight = 220,
                        Content = PromptDialog.Message(string.Join("\n", conflict.ExistingFiles))
                    }
                }
            };
            var dialog = PromptDialog.Create("Replace existing files?", body, 570, DialogArtwork.Warning, true,
                cancel, skip, replace);
            skip.Click += (_, _) => { decision = ConflictDecision.Skip; dialog.Close(); };
            replace.Click += (_, _) => { decision = ConflictDecision.Replace; dialog.Close(); };
            cancel.Click += (_, _) => dialog.Close();
            dialog.Opened += (_, _) => skip.Focus();
            using var registration = token.Register(() => Dispatcher.UIThread.Post(() => dialog.Close()));
            await ModalDialog.ShowAsync(dialog, owner);
            return decision;
        });
    }
}
