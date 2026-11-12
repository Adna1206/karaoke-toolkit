using System;
using System.Diagnostics;
using System.IO;

namespace KaraokeToolkit.App.Services;

public static class FolderLauncher
{
    public static void Open(string path)
    {
        string folder = Directory.Exists(path) ? Path.GetFullPath(path) : Path.GetDirectoryName(Path.GetFullPath(path))!;
        if (!Directory.Exists(folder))
            throw new DirectoryNotFoundException("The folder no longer exists: " + folder);
        var start = new ProcessStartInfo(OperatingSystem.IsWindows() ? "explorer.exe" : OperatingSystem.IsMacOS() ? "open" : "xdg-open") { UseShellExecute = false };
        start.ArgumentList.Add(folder);
        Process.Start(start)?.Dispose();
    }
}
