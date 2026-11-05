using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using KaraokeToolkit.Core.Conversion;

namespace KaraokeToolkit.Core.Recovery;

/// <summary>Deletes resolved transaction or completed checkpoint workspaces, journal last.</summary>
internal static class RecoveryWorkspaceCleanup
{
    internal static void Delete(string workspace, string journalName = "recovery.json")
    {
        workspace = Path.GetFullPath(workspace);
        string name = Path.GetFileName(workspace);
        string prefix = journalName switch
        {
            "recovery.json" => ".karaoke-job-",
            "run.json" => ".karaoke-job-run-",
            "workflow.json" => ".karaoke-job-workflow-",
            _ => throw new ArgumentException("Unknown recovery journal.", nameof(journalName))
        };
        if (!name.StartsWith(prefix, StringComparison.Ordinal) || !Guid.TryParseExact(name[prefix.Length..], "N", out _))
            throw new IOException("Refusing to clean an unrecognized recovery workspace: " + workspace);
        if (!Directory.Exists(workspace)) return;

        string journal = Path.Combine(workspace, journalName);
        byte[]? savedJournal = null;
        try
        {
            var files = new List<string>();
            var directories = new List<string>();
            Inspect(workspace, files, directories); // Refuse links before changing any attributes.
            if (File.Exists(journal)) savedJournal = File.ReadAllBytes(journal);
            foreach (string directory in directories) Retry(() => MakeWritable(directory));
            foreach (string file in files.Where(path => !InputSelection.Paths.Equals(path, journal)))
                Retry(() => DeleteFile(file));
            foreach (string directory in directories.AsEnumerable().Reverse().Where(path => !InputSelection.Paths.Equals(path, workspace)))
                Retry(() => { CheckPath(directory); Directory.Delete(directory); });
            Retry(() => DeleteFile(journal));
            Retry(() => { CheckPath(workspace); Directory.Delete(workspace); });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // If removing the final empty directory failed, put its journal back.
            // Never overwrite a journal another actor created in the meantime.
            if (savedJournal != null && Directory.Exists(workspace) && !File.Exists(journal))
            {
                try
                {
                    CheckPath(workspace);
                    using var stream = new FileStream(journal, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    stream.Write(savedJournal);
                    stream.Flush(flushToDisk: true);
                }
                catch (Exception restore) when (restore is IOException or UnauthorizedAccessException)
                {
                    throw new IOException("Recovery cleanup is incomplete and its journal could not be restored. Files remain at " + workspace,
                        new AggregateException(error, restore));
                }
            }
            throw new IOException("Recovery cleanup is incomplete. Any published outputs remain in place. Close programs using these files. Files remain at " + workspace, error);
        }
    }

    private static void Inspect(string directory, List<string> files, List<string> directories)
    {
        CheckPath(directory);
        directories.Add(directory);
        foreach (string entry in Directory.GetFileSystemEntries(directory))
        {
            CheckPath(entry);
            if (Directory.Exists(entry)) Inspect(entry, files, directories);
            else files.Add(entry);
        }
    }

    private static void MakeWritable(string path)
    {
        CheckPath(path);
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReadOnly) != 0)
            File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
    }

    private static void DeleteFile(string path)
    {
        if (!File.Exists(path)) return;
        MakeWritable(path);
        File.Delete(path);
    }

    private static void Retry(Action action)
    {
        for (int attempt = 0; ; attempt++)
        {
            try { action(); return; }
            catch (Exception error) when (attempt < 4 && (error is IOException or UnauthorizedAccessException))
            { Thread.Sleep(25 * (attempt + 1)); }
        }
    }

    private static void CheckPath(string path)
    {
        for (string? current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) == 0) continue;
            // Cloud placeholders do not redirect names. Junctions, symlinks and
            // unrecognized reparse providers remain forbidden during cleanup.
            if (!OperatingSystem.IsWindows() || !IsCloudTag(GetReparseTag(current)))
                throw new IOException("Transaction cleanup cannot follow linked paths: " + current);
        }
    }

    internal static bool IsCloudTag(uint tag) => (tag & 0xffff0fff) == 0x9000001a;

    private static uint GetReparseTag(string path)
    {
        if (!path.StartsWith(@"\\?\", StringComparison.Ordinal))
            path = path.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + path[2..] : @"\\?\" + path;
        // OPEN_EXISTING; OPEN_REPARSE_POINT | BACKUP_SEMANTICS. Query metadata
        // on the entry itself without following it or opening its file contents.
        using var handle = CreateFileW(path, 0, 7, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
        if (handle.IsInvalid || !GetFileInformationByHandleEx(handle, 9, out var info, 8))
            throw new IOException("Cannot inspect transaction cleanup path: " + path,
                new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));
        return info.ReparseTag;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AttributeTagInfo
    {
        public uint Attributes;
        public uint ReparseTag;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int informationClass, out AttributeTagInfo info, uint size);
}
