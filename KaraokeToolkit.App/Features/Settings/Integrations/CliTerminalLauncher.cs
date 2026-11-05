using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace KaraokeToolkit.App.Features.Settings.Integrations;

/// <summary>Opens an interactive terminal without evaluating copied examples or elevating.</summary>
public static class CliTerminalLauncher
{
    public static void Open(string directory)
    {
        using var process = Process.Start(CreateStartInfo(directory)) ?? throw new IOException("The terminal could not be started.");
    }

    internal static ProcessStartInfo CreateStartInfo(string directory)
    {
        directory = Path.GetFullPath(directory);
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("Application folder is unavailable.");
        var start = new ProcessStartInfo { WorkingDirectory = directory };
        if (OperatingSystem.IsWindows())
        {
            start.FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
            start.UseShellExecute = true;
            start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-NoExit");
        }
        else if (OperatingSystem.IsMacOS())
        {
            start.FileName = "/usr/bin/open";
            start.ArgumentList.Add("-a"); start.ArgumentList.Add("Terminal"); start.ArgumentList.Add(directory);
        }
        else
        {
            string[] names = ["x-terminal-emulator", "gnome-terminal", "konsole", "xfce4-terminal", "xterm"];
            var paths = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Where(p => !string.IsNullOrWhiteSpace(p));
            start.FileName = names.SelectMany(name => paths.Select(p => Path.Combine(p, name))).FirstOrDefault(File.Exists)
                ?? throw new IOException("No supported terminal was found. Copy a command and open your terminal manually.");
            string terminal = Path.GetFileName(start.FileName);
            if (terminal == "gnome-terminal") { start.ArgumentList.Add("--working-directory"); start.ArgumentList.Add(directory); }
            else if (terminal == "konsole") { start.ArgumentList.Add("--workdir"); start.ArgumentList.Add(directory); }
            else if (terminal == "xfce4-terminal") start.ArgumentList.Add("--working-directory=" + directory);
        }
        return start;
    }
}
