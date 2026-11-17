using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using KaraokeToolkit.App.Shared;
using Avalonia.Interactivity;

namespace KaraokeToolkit.App.Features.Settings.Integrations;

public partial class McpIntegrationView : UserControl
{
    private readonly McpIntegrationViewModel model = new();
    public McpIntegrationView() { InitializeComponent(); DataContext = model; }
    internal Action<string, StatusKind>? ReportStatus { get; set; }

    private async Task Copy(string text)
    {
        await model.CopyAsync(text, async value =>
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard ?? throw new InvalidOperationException("Clipboard is unavailable.");
            await clipboard.SetTextAsync(value);
        });
        ReportStatus?.Invoke(model.Status, model.StatusSeverity);
    }
    private async void CopyPath_Click(object? sender, RoutedEventArgs e) => await Copy(model.ExecutablePath);
    private async void CopyConfiguration_Click(object? sender, RoutedEventArgs e) => await Copy(model.ConfigurationJson);
}
