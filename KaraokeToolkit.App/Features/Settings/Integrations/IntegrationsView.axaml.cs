using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using KaraokeToolkit.App.Services;

namespace KaraokeToolkit.App.Features.Settings.Integrations;

public partial class IntegrationsView : UserControl
{
    public IntegrationsView()
    {
        InitializeComponent();
        ExplorerSection.IsVisible = OperatingSystem.IsWindows();
        if (ExplorerSection.IsVisible) RefreshExplorer();
    }

    private void RefreshExplorer()
    {
        try
        {
            ExplorerStatus.Text = new ExplorerIntegration().IsInstalled() ? "Enabled for this application location." : "Not enabled for this application location.";
        }
        catch (Exception error) { ExplorerStatus.Text = error.Message; }
    }
    private void EnableExplorer_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            new ExplorerIntegration().Install();
            RefreshExplorer();
        }
        catch (Exception error) { ExplorerStatus.Text = "Could not enable Explorer integration: " + error.Message; }
    }
    private void RemoveExplorer_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            new ExplorerIntegration().Remove();
            RefreshExplorer();
        }
        catch (Exception error) { ExplorerStatus.Text = "Could not remove Explorer integration: " + error.Message; }
    }
}
