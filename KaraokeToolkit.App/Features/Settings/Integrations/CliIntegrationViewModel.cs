using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using KaraokeToolkit.App.Shared;
using KaraokeToolkit.Core.Execution;

namespace KaraokeToolkit.App.Features.Settings.Integrations;

public enum CliShell { PowerShell, Bash, Zsh }

/// <summary>CLI discovery and copyable instructions, independent of processing preferences.</summary>
public partial class CliIntegrationViewModel : ViewModelBase
{
    public string ExecutablePath { get; }
    public bool IsAvailable { get; }
    public bool IsMissing => !IsAvailable;
    [ObservableProperty] public partial StatusKind StatusSeverity { get; private set; }
    public bool HasStatus => !string.IsNullOrWhiteSpace(Status);
    public bool ExampleNeedsFolder => ExampleIndex is 2 or 3;
    public string Availability => IsAvailable ? "Included with this application." : "CLI not found beside this build. Published app packages include it.";
    public IReadOnlyList<CliShell> Shells { get; } = Enum.GetValues<CliShell>();
    public IReadOnlyList<string> Examples { get; } = ["Show help", "List operations", "Validate a folder", "Preview tags"];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CommandText))]
    [NotifyPropertyChangedFor(nameof(SetupText))]
    public partial CliShell Shell { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CommandText))]
    [NotifyPropertyChangedFor(nameof(ExampleNeedsFolder))]
    public partial int ExampleIndex { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    public partial string Status { get; set; } = "";

    public CliIntegrationViewModel() : this(AppContext.BaseDirectory, OperatingSystem.IsWindows(),
        OperatingSystem.IsMacOS() ? CliShell.Zsh : OperatingSystem.IsWindows() ? CliShell.PowerShell : CliShell.Bash) { }

    public CliIntegrationViewModel(string directory, bool windows, CliShell shell, Func<string, bool>? exists = null)
    {
        ExecutablePath = Path.Combine(Path.GetFullPath(directory), windows ? "karaoke-toolkit.exe" : "karaoke-toolkit");
        IsAvailable = (exists ?? File.Exists)(ExecutablePath);
        Shell = shell;
    }

    public string CommandText => Invocation + (ExampleIndex switch
    {
        1 => " operations",
        2 => $" {OperationCommandNames.Get(OperationId.ValidateKaraoke)} '<karaoke-folder>' --json --quiet",
        3 => $" {OperationCommandNames.Get(OperationId.PreviewTags)} '<karaoke-folder>' --json --quiet",
        _ => " --help"
    });

    private string Invocation => (Shell == CliShell.PowerShell ? "& " : "") + Quote(ExecutablePath);
    private string Quote(string value) => "'" + value.Replace("'", Shell == CliShell.PowerShell ? "''" : "'\"'\"'") + "'";
    private string ShellName => Shell.ToString().ToLowerInvariant();

    public string SetupText => Shell == CliShell.PowerShell
        ? $"# Current session; review the generated scripts before evaluating them.\n({Invocation} completion powershell | Out-String) | Invoke-Expression\n({Invocation} path powershell | Out-String) | Invoke-Expression"
        : $"# Current session; review the generated scripts before evaluating them.\neval \"$({Invocation} completion {ShellName})\"\neval \"$({Invocation} path {ShellName})\"";

    public async Task CopyAsync(string text, Func<string, Task> copy)
    {
        try { await copy(text); StatusSeverity = StatusKind.Success; Status = "Copied to clipboard."; }
        catch (Exception error) { StatusSeverity = StatusKind.Failure; Status = "Could not copy: " + error.Message; }
    }

    public void OpenTerminal(Action<string> open)
    {
        if (!IsAvailable) { StatusSeverity = StatusKind.Warning; Status = Availability; return; }
        try { open(Path.GetDirectoryName(ExecutablePath)!); StatusSeverity = StatusKind.Success; Status = "Terminal opened in the application folder. No CLI command was run."; }
        catch (Exception error) { StatusSeverity = StatusKind.Failure; Status = "Could not open a terminal: " + error.Message; }
    }
}
