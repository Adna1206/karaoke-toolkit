using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using KaraokeToolkit.App.Shared;

namespace KaraokeToolkit.App.Features.Settings.Integrations;

/// <summary>Builds client configuration only; never starts MCP or changes its runtime policy.</summary>
public partial class McpIntegrationViewModel : ViewModelBase
{
    public string ExecutablePath { get; }
    public bool IsAvailable { get; }
    public bool IsMissing => !IsAvailable;
    [ObservableProperty] public partial StatusKind StatusSeverity { get; private set; }
    public bool HasStatus => !string.IsNullOrWhiteSpace(Status);
    public string Availability => "MCP not found beside this build. Published app packages include it.";
    public string[] FileAccessModes { get; } = ["Read only", "Allow file changes"];
    public bool AllowFileChanges => FileAccess == FileAccessModes[1];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConfigurationJson))]
    [NotifyPropertyChangedFor(nameof(AllowFileChanges))]
    public partial string FileAccess { get; set; } = "Read only";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    public partial string Status { get; set; } = "";

    public McpIntegrationViewModel() : this(AppContext.BaseDirectory, OperatingSystem.IsWindows()) { }
    public McpIntegrationViewModel(string directory, bool windows, Func<string, bool>? exists = null)
    {
        ExecutablePath = Path.Combine(Path.GetFullPath(directory), windows ? "karaoke-toolkit-mcp.exe" : "karaoke-toolkit-mcp");
        IsAvailable = (exists ?? File.Exists)(ExecutablePath);
    }

    public string ConfigurationJson => JsonSerializer.Serialize(new
    {
        mcpServers = new Dictionary<string, object>
        {
            ["karaoke-toolkit"] = new { command = ExecutablePath, args = AllowFileChanges ? new[] { "--allow-writes" } : Array.Empty<string>() }
        }
    }, new JsonSerializerOptions { WriteIndented = true });

    public async Task CopyAsync(string text, Func<string, Task> copy)
    {
        try { await copy(text); StatusSeverity = StatusKind.Success; Status = "Copied to clipboard."; }
        catch (Exception error) { StatusSeverity = StatusKind.Failure; Status = "Could not copy: " + error.Message; }
    }
}
