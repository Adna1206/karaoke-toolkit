using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input.Platform;

namespace KaraokeToolkit.App.Features.Settings.Integrations;

public partial class CliIntegrationView : UserControl
{
    private readonly CliIntegrationViewModel model = new();
    public CliIntegrationView() { InitializeComponent(); DataContext = model; }
    private Task Copy(string text) => model.CopyAsync(text, async value =>
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard ?? throw new InvalidOperationException("Clipboard is unavailable.");
        await clipboard.SetTextAsync(value);
    });
    private async void CopyPath_Click(object? sender, RoutedEventArgs e) => await Copy(model.ExecutablePath);
    private async void CopyCommand_Click(object? sender, RoutedEventArgs e) => await Copy(model.CommandText);
    private async void CopySetup_Click(object? sender, RoutedEventArgs e) => await Copy(model.SetupText);
    private void OpenTerminal_Click(object? sender, RoutedEventArgs e) => model.OpenTerminal(CliTerminalLauncher.Open);
}
