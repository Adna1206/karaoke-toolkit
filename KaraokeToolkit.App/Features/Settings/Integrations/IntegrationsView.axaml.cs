using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using KaraokeToolkit.App.Services;
using KaraokeToolkit.App.Shared;

namespace KaraokeToolkit.App.Features.Settings.Integrations;

public partial class IntegrationsView : UserControl
{
    public IntegrationsView()
    {
        InitializeComponent();
        CliIntegration.ReportStatus = ShowStatus;
        McpIntegration.ReportStatus = ShowStatus;
        ExplorerSection.IsVisible = OperatingSystem.IsWindows();
        if (ExplorerSection.IsVisible)
        {
            try { RefreshExplorer(); }
            catch (Exception error) { ExplorerStatus.Text = "Could not read Explorer integration: " + error.Message; }
        }
    }

    private void RefreshExplorer()
    {
        bool enabled = new ExplorerIntegration().IsInstalled();
        ExplorerStatus.Text = enabled ? "Enabled for this application location." : "Not enabled for this application location.";
    }
    private void EnableExplorer_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            new ExplorerIntegration().Install();
            RefreshExplorer();
            ShowStatus("Explorer integration enabled.", StatusKind.Success);
        }
        catch (Exception error) { ShowStatus("Could not enable Explorer integration: " + error.Message, StatusKind.Failure); }
    }
    private void RemoveExplorer_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            new ExplorerIntegration().Remove();
            RefreshExplorer();
            ShowStatus("Explorer integration removed.", StatusKind.Success);
        }
        catch (Exception error) { ShowStatus("Could not remove Explorer integration: " + error.Message, StatusKind.Failure); }
    }

    private void ShowStatus(string message, StatusKind kind) =>
        (DataContext as SettingsViewModel)?.ShowStatus(message, kind);
}
