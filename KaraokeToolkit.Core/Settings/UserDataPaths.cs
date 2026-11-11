namespace KaraokeToolkit.Core.Settings;

/// <summary>Resolves per-user data independently of the Windows Setup installation directory.</summary>
public static class UserDataPaths
{
    private static string LocalData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    public static string DesktopSettingsPath() => DesktopSettingsPath(LocalData, OperatingSystem.IsWindows());

    internal static string DesktopSettingsPath(string localData, bool windows)
    {
        string root = DataRoot(localData, windows);
        if (windows)
        {
            string legacy = DataRoot(localData, windows: false);
            string marker = Path.Combine(root, ".desktop-migrated");
            if (Directory.Exists(legacy) && !File.Exists(marker))
            {
                CopyIfMissing(Path.Combine(legacy, "options.xmls"), Path.Combine(root, "options.xmls"));
                CopyDirectory(Path.Combine(legacy, "Recipes"), Path.Combine(root, "Recipes"));
                MarkMigrated(marker);
            }
        }
        return Path.Combine(root, "options.xmls");
    }

    internal static string RecoveryDirectory() => RecoveryDirectory(LocalData, OperatingSystem.IsWindows());

    internal static string RecoveryDirectory(string localData, bool windows)
    {
        string directory = Path.Combine(DataRoot(localData, windows), "Recovery");
        if (windows)
        {
            string legacy = Path.Combine(DataRoot(localData, windows: false), "Recovery");
            string marker = Path.Combine(directory, ".migrated");
            if (Directory.Exists(legacy) && !File.Exists(marker))
            {
                CopyDirectory(legacy, directory);
                MarkMigrated(marker);
            }
        }
        return directory;
    }

    private static string DataRoot(string localData, bool windows) =>
        Path.Combine(localData, windows ? "KaraokeToolkitData" : "KaraokeToolkit");

    private static void CopyDirectory(string source, string destination)
    {
        if (!Directory.Exists(source)) return;
        foreach (string file in Directory.EnumerateFiles(source, "*.json"))
            CopyIfMissing(file, Path.Combine(destination, Path.GetFileName(file)));
    }

    private static void CopyIfMissing(string source, string destination)
    {
        if (!File.Exists(source) || File.Exists(destination)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var input = File.OpenRead(source))
            using (var output = new FileStream(temporary, FileMode.CreateNew))
            {
                input.CopyTo(output);
                output.Flush(flushToDisk: true);
            }
            try { File.Move(temporary, destination); }
            catch (IOException) when (File.Exists(destination)) { /* Another process migrated or saved it. */ }
        }
        finally { File.Delete(temporary); }
    }

    private static void MarkMigrated(string marker)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
        using var file = new FileStream(marker, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite);
    }
}
