using System.ComponentModel;
using System.Runtime.InteropServices;

namespace KaraokeToolkit.Core.Recovery;

/// <summary>Atomic rename without overwrite or cross-volume copy/delete fallback.</summary>
internal static class GroupingRename
{
    internal static void Move(string source, string target)
    {
        if (OperatingSystem.IsWindows())
        {
            if (MoveFile(source, target, 0)) return;
        }
        else if (OperatingSystem.IsLinux())
        {
            if (RenameAt2(-100, source, -100, target, 1) == 0) return;
        }
        else if (OperatingSystem.IsMacOS())
        {
            if (RenameExclusive(source, target, 4) == 0) return;
        }
        else throw new PlatformNotSupportedException("Atomic grouping publication is unavailable on this platform.");

        int error = Marshal.GetLastPInvokeError();
        var cause = new Win32Exception(error);
        throw new IOException($"Cannot move '{source}' to '{target}': {cause.Message}", cause);
    }

    [DllImport("kernel32.dll", EntryPoint = "MoveFileExW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveFile(string source, string target, uint flags);

    [DllImport("libc", EntryPoint = "renameat2", SetLastError = true)]
    private static extern int RenameAt2(int sourceDirectory, [MarshalAs(UnmanagedType.LPUTF8Str)] string source,
        int targetDirectory, [MarshalAs(UnmanagedType.LPUTF8Str)] string target, uint flags);

    [DllImport("libSystem.B.dylib", EntryPoint = "renamex_np", SetLastError = true)]
    private static extern int RenameExclusive([MarshalAs(UnmanagedType.LPUTF8Str)] string source,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string target, uint flags);
}
