using System.Text.Json;

namespace KaraokeToolkit.Core.Recovery;

internal sealed record RecoveryOutput(string Name, string Hash);
internal sealed record RecoveryBackup(string Name, string Slot, string Hash);
internal sealed class TransactionJournal
{
    public int Version { get; set; } = 1;
    public string Phase { get; set; } = "Publishing";
    public RecoveryOutput[] Outputs { get; set; } = [];
    public RecoveryBackup[] Backups { get; set; } = [];
}

internal static class FileTransactionRecovery
{
    // Journals contain only leaf names. Never trust paths supplied by a journal.
    private static string Leaf(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.IndexOfAny(['/', '\\', ':']) >= 0 || Path.GetFileName(name) != name)
            throw new IOException("Invalid filename in recovery journal.");
        return name;
    }

    internal static IReadOnlyList<string>? Recover(string workspace)
    {
        RunSession.CheckPlainPath(workspace);
        string folder = Path.GetDirectoryName(workspace)!;
        using var lease = FileTransaction.AcquireLease(folder);
        string path = Path.Combine(workspace, "recovery.json");
        if (!File.Exists(path))
            return null; // Interrupted staging: originals were never moved.
        RunSession.CheckPlainPath(path);
        var journal = JsonSerializer.Deserialize<TransactionJournal>(File.ReadAllText(path)) ?? throw new IOException("Empty transaction journal.");
        if (journal.Version != 1 || journal.Outputs == null || journal.Backups == null || journal.Outputs.Length == 0 ||
            journal.Outputs.Any(x => x == null) || journal.Backups.Any(x => x == null) ||
            journal.Phase is not ("Publishing" or "Committed" or "RollingBack" or "RolledBack"))
            throw new IOException("This journal needs manual recovery: " + workspace);
        foreach (var item in journal.Outputs)
        {
            Leaf(item.Name);
            ValidateHash(item.Hash);
        }
        foreach (var item in journal.Backups)
        {
            Leaf(item.Name);
            Leaf(item.Slot);
            ValidateHash(item.Hash);
        }
        if (journal.Outputs.Select(x => x.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != journal.Outputs.Length ||
            journal.Backups.Select(x => x.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != journal.Backups.Length ||
            journal.Backups.Select(x => x.Slot).Distinct(StringComparer.OrdinalIgnoreCase).Count() != journal.Backups.Length)
            throw new IOException("Duplicate recovery entries.");
        if (journal.Phase is "RollingBack" or "RolledBack")
        {
            Rollback(workspace, journal);
            return null;
        }

        // Preflight every file before moving anything. An unrelated or edited file blocks recovery.
        foreach (var old in journal.Backups)
        {
            string backup = Path.Combine(workspace, "backups", old.Slot), original = Path.Combine(folder, old.Name);
            if (Exists(backup))
            {
                RequireHash(backup, old.Hash);
                if (Exists(original) && !journal.Outputs.Any(x => x.Name.Equals(old.Name, StringComparison.OrdinalIgnoreCase)))
                    throw new IOException("A source reappeared during recovery: " + original);
            }
            else
                RequireHash(original, old.Hash);
        }
        foreach (var output in journal.Outputs)
        {
            string staged = Path.Combine(workspace, output.Name), target = Path.Combine(folder, output.Name);
            if (Exists(staged))
            {
                RequireHash(staged, output.Hash);
                var old = journal.Backups.FirstOrDefault(x => x.Name.Equals(output.Name, StringComparison.OrdinalIgnoreCase));
                if (Exists(target) && (old == null || Exists(Path.Combine(workspace, "backups", old.Slot))))
                    throw new IOException("A new file blocks recovery: " + target);
            }
            else
                RequireHash(target, output.Hash);
        }
        foreach (var old in journal.Backups)
        {
            string backup = Path.Combine(workspace, "backups", old.Slot), original = Path.Combine(folder, old.Name);
            if (!File.Exists(backup))
                File.Move(original, backup);
            else if (Exists(original) && !journal.Outputs.Any(x => x.Name.Equals(old.Name, StringComparison.OrdinalIgnoreCase)))
                throw new IOException("A source reappeared during recovery: " + original);
        }
        foreach (var output in journal.Outputs)
        {
            string staged = Path.Combine(workspace, output.Name), target = Path.Combine(folder, output.Name);
            if (File.Exists(staged))
                File.Move(staged, target);
            RequireHash(target, output.Hash);
        }
        journal.Phase = "Committed";
        DurableJson.Write(path, journal);
        return journal.Outputs.Select(x => Path.Combine(folder, x.Name)).ToArray();
    }

    internal static void Rollback(string workspace, TransactionJournal journal)
    {
        string folder = Path.GetDirectoryName(workspace)!;
        // This is also restartable: restored originals have their original hash and no backup.
        foreach (var old in journal.Backups)
        {
            string backup = Path.Combine(workspace, "backups", old.Slot);
            RequireHash(File.Exists(backup) ? backup : Path.Combine(folder, old.Name), old.Hash);
            if (File.Exists(backup) && Exists(Path.Combine(folder, old.Name)) && !journal.Outputs.Any(x => x.Name.Equals(old.Name, StringComparison.OrdinalIgnoreCase)))
                throw new IOException("A source reappeared during rollback: " + old.Name);
        }
        foreach (var output in journal.Outputs)
        {
            string target = Path.Combine(folder, output.Name), staged = Path.Combine(workspace, output.Name);
            if (!Exists(target))
                continue;
            var old = journal.Backups.FirstOrDefault(x => x.Name.Equals(output.Name, StringComparison.OrdinalIgnoreCase));
            if (old != null && !File.Exists(Path.Combine(workspace, "backups", old.Slot)))
                RequireHash(target, old.Hash);
            else if (!File.Exists(staged))
                RequireHash(target, output.Hash);
            else
                throw new IOException("An unexpected output blocks rollback: " + target);
        }
        journal.Phase = "RollingBack";
        DurableJson.Write(Path.Combine(workspace, "recovery.json"), journal);
        foreach (var output in journal.Outputs)
        {
            string target = Path.Combine(folder, output.Name);
            var old = journal.Backups.FirstOrDefault(x => x.Name.Equals(output.Name, StringComparison.OrdinalIgnoreCase));
            if (File.Exists(target) && (old == null || File.Exists(Path.Combine(workspace, "backups", old.Slot))))
                File.Delete(target);
        }
        foreach (var old in journal.Backups)
        {
            string backup = Path.Combine(workspace, "backups", old.Slot);
            if (File.Exists(backup))
                File.Move(backup, Path.Combine(folder, old.Name));
        }
        journal.Phase = "RolledBack";
        DurableJson.Write(Path.Combine(workspace, "recovery.json"), journal);
    }

    private static bool Exists(string path)
    {
        RunSession.CheckPlainPath(path);
        if (Directory.Exists(path))
            throw new IOException("A directory blocks recovery: " + path);
        return File.Exists(path);
    }
    private static void RequireHash(string path, string hash)
    {
        if (!Exists(path) || FileTransaction.Hash(path) != hash)
            throw new IOException("Recovery stopped: file missing or changed: " + path);
    }
    private static void ValidateHash(string hash)
    {
        if (string.IsNullOrEmpty(hash) || hash.Length != 64 || hash.Any(c => !Uri.IsHexDigit(c)))
            throw new IOException("Invalid recovery hash.");
    }
}
