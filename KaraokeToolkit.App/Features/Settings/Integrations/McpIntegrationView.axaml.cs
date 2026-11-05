using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;

namespace KaraokeToolkit.App.Features.Settings.Integrations;

public partial class McpIntegrationView : UserControl
{
    private readonly McpIntegrationViewModel model = new();
    public McpIntegrationView() { InitializeComponent(); DataContext = model; }
    private Task Copy(string text) => model.CopyAsync(text, async value =>
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard ?? throw new InvalidOperationException("Clipboard is unavailable.");
        await clipboard.SetTextAsync(value);
    });
    private async void CopyPath_Click(object? sender, RoutedEventArgs e) => await Copy(model.ExecutablePath);
    private async void CopyConfiguration_Click(object? sender, RoutedEventArgs e) => await Copy(model.ConfigurationJson);
}
