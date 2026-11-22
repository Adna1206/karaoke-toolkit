using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace KaraokeToolkit.App.Shared;

internal static class PromptDialog
{
    public static Window Create(string heading, Control body, double width, string artwork, bool warning, params Button[] buttons)
    {
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = WindowSpacing.FooterButtonGap
        };
        actions.Children.AddRange(buttons);
        return new Window
        {
            Title = WindowTitles.For(heading),
            Width = width,
            CanResize = false,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = WindowSpacing.ContentInset,
                Spacing = 14,
                Children =
                {
                    DialogArtwork.Beside(new StackPanel
                    {
                        Spacing = 8,
                        Children =
                        {
                            new TextBlock { Text = heading, TextWrapping = TextWrapping.Wrap, Classes = { "heading2" } },
                            body
                        }
                    }, artwork, warning),
                    new Border { Classes = { "windowFooter", "dialog" }, Child = actions }
                }
            }
        };
    }

    public static async Task<bool> ConfirmAsync(Window owner, string heading, string message, string action,
        bool destructive = false, string? details = null, double width = 480)
    {
        var body = new StackPanel { Spacing = 10, Children = { Message(message) } };
        if (!string.IsNullOrEmpty(details))
        {
            body.Children.Add(new ScrollViewer
            {
                MaxHeight = 220,
                Content = Message(details)
            });
        }

        var cancel = new Button { Content = "Cancel" };
        var confirm = new Button { Content = action, Classes = { destructive ? "destructive" : "accent" } };
        var dialog = Create(heading, body, width, DialogArtwork.Warning, destructive, cancel, confirm);
        cancel.Click += (_, _) => dialog.Close(false);
        confirm.Click += (_, _) => dialog.Close(true);
        dialog.Opened += (_, _) => cancel.Focus();
        return await ModalDialog.ShowAsync<bool?>(dialog, owner) == true;
    }

    public static TextBlock Message(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap };
}
