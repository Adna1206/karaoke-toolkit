using KaraokeToolkit.Core.Conversion;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using KaraokeToolkit.Core;

namespace KaraokeToolkit.App.Services;

public sealed record ExplorerMenuEntry(string Key, string Command);

/// <summary>Per-user classic Explorer verbs. Never changes a file's default application.</summary>
public sealed class ExplorerIntegration
{
    private const string Owner = "KaraokeToolkit.Explorer.v1";
    private readonly string classesRoot;
    private readonly string executable;
    public ExplorerIntegration(string? executable = null, string registryRoot = @"Software\Classes")
    {
        this.executable = executable ?? AppExecutable;
        classesRoot = registryRoot.TrimEnd('\\') + "\\";
    }
    public static string MenuLabel => "Open in " + ApplicationInfo.Name;

    public static string AppExecutable => Path.Combine(AppContext.BaseDirectory, "KaraokeToolkit.App.exe");

    public static IReadOnlyList<ExplorerMenuEntry> Entries(string executable)
    {
        if (!Path.IsPathFullyQualified(executable) || executable.IndexOfAny(['"', '\r', '\n', '\0']) >= 0)
            throw new ArgumentException("An absolute application path is required.");
        string command = "\"" + executable + "\" --open \"%1\"";
        return new[]
        {
            new ExplorerMenuEntry(@"Directory\shell\KaraokeToolkit", command),
            new ExplorerMenuEntry(@"Directory\Background\shell\KaraokeToolkit", "\"" + executable + "\" --open \"%V\"")
        }.Concat(InputSelection.Extensions.Select(ext => new ExplorerMenuEntry(@"SystemFileAssociations\" + ext + @"\shell\KaraokeToolkit", command))).ToArray();
    }

    public bool IsInstalled()
    {
        if (!OperatingSystem.IsWindows())
            return false;
        foreach (var entry in Entries(executable))
        {
            using var key = Registry.CurrentUser.OpenSubKey(classesRoot + entry.Key);
            using var command = key?.OpenSubKey("command");
            if (key?.GetValue("Owner") as string != Owner || command?.GetValue("") as string != entry.Command)
                return false;
        }
        return true;
    }

    public void Install()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Explorer integration is available on Windows.");
        if (!File.Exists(executable))
            throw new FileNotFoundException("Build or publish the app executable before enabling Explorer integration.", executable);
        var entries = Entries(executable);
        foreach (var entry in entries)
        {
            using var existing = Registry.CurrentUser.OpenSubKey(classesRoot + entry.Key);
            if (existing != null && existing.GetValue("Owner") as string != Owner)
                throw new IOException("An unrecognized Explorer entry already uses this name: " + entry.Key);
        }
        foreach (var entry in entries)
        {
            using var key = Registry.CurrentUser.CreateSubKey(classesRoot + entry.Key);
            key.SetValue("Owner", Owner);
            key.SetValue("", MenuLabel);
            key.SetValue("Icon", "\"" + executable + "\",0");
            key.SetValue("MultiSelectModel", "Single");
            using var command = key.CreateSubKey("command");
            command.SetValue("", entry.Command);
        }
    }

    public void Remove()
    {
        if (!OperatingSystem.IsWindows())
            return;
        foreach (var entry in Entries(executable))
        {
            using var key = Registry.CurrentUser.OpenSubKey(classesRoot + entry.Key);
            bool owned = key?.GetValue("Owner") as string == Owner;
            key?.Dispose();
            if (owned)
                Registry.CurrentUser.DeleteSubKeyTree(classesRoot + entry.Key, throwOnMissingSubKey: false);
        }
    }

    public static string[] ParseInputs(string[] arguments)
    {
        if (arguments.Length == 0)
            return [];
        var inputs = arguments[0] == "--open" ? arguments.Skip(1).ToArray() : arguments;
        if (inputs.Length == 0)
            throw new ArgumentException("No files or folders were provided.");
        return InputSelection.Normalize(inputs);
    }
}
