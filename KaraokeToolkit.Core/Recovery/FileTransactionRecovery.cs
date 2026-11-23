using System.Text.Json;

namespace KaraokeToolkit.Core.Recovery;

internal sealed record RecoveryOutput(string Name, string Hash);
internal sealed record RecoveryBackup(string Name, string Slot, string Hash);
internal sealed class TransactionJournal
{
    public string VerificationMode { get; set; } = RecoveryFingerprint.Sha256;
    public int Version { get; set; } = 1;
    public string Phase { get; set; } = "Publishing";
    public RecoveryOutput[] Outputs { get; set; } = [];
    public RecoveryBackup[] Backups { get; set; } = [];
}

/// <summary>Reconciles journaled file-group publication or rollback against recorded content hashes.</summary>
internal static class FileTransactionRecovery
{
    // Journals contain only leaf names. Never trust paths supplied by a journal.
    private static string Leaf(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.IndexOfAny(['/', '\\', ':']) >= 0 || Path.GetFileName(name) != name)
            throw new IOException("Invalid filename in recovery journal.");
        return name;
    }

    /// <summary>Finishes interrupted publication, or resumes rollback when its journal requests it.</summary>
    /// <returns>Verified published paths, or null for interrupted staging or a rolled-back group.</returns>
    /// <remarks>Retains the workspace for owner receipts and subsequent cleanup. Repeated recovery is supported;
    /// edited files or unsafe journal paths block recovery rather than being overwritten.</remarks>
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
        if (!RecoveryFingerprint.IsContentVerified(RecoveryFingerprint.Parse(journal.VerificationMode)))
            throw new IOException("Interrupted publication has no content fingerprints. Keep the workspace for manual recovery: " + workspace);
        if (journal.Version != 1 || journal.Outputs == null || journal.Backups == null || journal.Outputs.Length == 0 ||
            journal.Outputs.Any(x => x == null) || journal.Backups.Any(x => x == null) ||
            journal.Phase is not ("Publishing" or "Committed" or "RollingBack" or "RolledBack"))
            throw new IOException("This journal needs manual recovery: " + workspace);
        foreach (var item in journal.Outputs)
        {
            Leaf(item.Name);
            ValidateHash(item.Hash, journal.VerificationMode);
        }
        foreach (var item in journal.Backups)
        {
            Leaf(item.Name);
            Leaf(item.Slot);
            ValidateHash(item.Hash, journal.VerificationMode);
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
        if (journal.Phase == "Committed")
        {
            // Publication is durable. Cleanup may already have deleted some old
            // backups; do not require deleted originals or attempt publication again.
            foreach (var output in journal.Outputs)
                RequireHash(Path.Combine(folder, output.Name), output.Hash, journal.VerificationMode);
            foreach (var backup in journal.Backups)
            {
                string old = Path.Combine(workspace, "backups", backup.Slot);
                if (Exists(old)) RequireHash(old, backup.Hash, journal.VerificationMode);
            }
            return journal.Outputs.Select(output => Path.Combine(folder, output.Name)).ToArray();
        }

        // Preflight every file before moving anything. An unrelated or edited file blocks recovery.
        foreach (var old in journal.Backups)
        {
            string backup = Path.Combine(workspace, "backups", old.Slot), original = Path.Combine(folder, old.Name);
            if (Exists(backup))
            {
                RequireHash(backup, old.Hash, journal.VerificationMode);
                if (Exists(original) && !journal.Outputs.Any(x => x.Name.Equals(old.Name, StringComparison.OrdinalIgnoreCase)))
                    throw new IOException("A source reappeared during recovery: " + original);
            }
            else
                RequireHash(original, old.Hash, journal.VerificationMode);
        }
        foreach (var output in journal.Outputs)
        {
            string staged = Path.Combine(workspace, output.Name), target = Path.Combine(folder, output.Name);
            if (Exists(staged))
            {
                RequireHash(staged, output.Hash, journal.VerificationMode);
                var old = journal.Backups.FirstOrDefault(x => x.Name.Equals(output.Name, StringComparison.OrdinalIgnoreCase));
                if (Exists(target) && (old == null || Exists(Path.Combine(workspace, "backups", old.Slot))))
                    throw new IOException("A new file blocks recovery: " + target);
            }
            else
                RequireHash(target, output.Hash, journal.VerificationMode);
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
            RequireHash(target, output.Hash, journal.VerificationMode);
        }
        journal.Phase = "Committed";
        DurableJson.Write(path, journal);
        return journal.Outputs.Select(x => Path.Combine(folder, x.Name)).ToArray();
    }

    /// <summary>Restores verified originals and removes verified new outputs, recording restartable rollback progress.</summary>
    /// <remarks>Preflights the group before moving files; unexpected content blocks restoration.
    /// Leaves the journal and workspace for the caller to retain or clean.</remarks>
    internal static void Rollback(string workspace, TransactionJournal journal)
    {
        if (!RecoveryFingerprint.IsContentVerified(RecoveryFingerprint.Parse(journal.VerificationMode)))
            throw new IOException("Interrupted publication has no content fingerprints. Keep the workspace for manual recovery: " + workspace);
        string folder = Path.GetDirectoryName(workspace)!;
        // This is also restartable: restored originals have their original hash and no backup.
        foreach (var old in journal.Backups)
        {
            string backup = Path.Combine(workspace, "backups", old.Slot);
            RequireHash(File.Exists(backup) ? backup : Path.Combine(folder, old.Name), old.Hash, journal.VerificationMode);
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
                RequireHash(target, old.Hash, journal.VerificationMode);
            else if (!File.Exists(staged))
                RequireHash(target, output.Hash, journal.VerificationMode);
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
    private static void RequireHash(string path, string hash, string mode)
    {
        if (!Exists(path) || RecoveryFingerprint.Hash(path, mode) != hash)
            throw new IOException("Recovery stopped: file missing or changed: " + path);
    }
    private static void ValidateHash(string hash, string mode)
    {
        if (string.IsNullOrEmpty(hash) || !RecoveryFingerprint.IsValid(hash, RecoveryFingerprint.Parse(mode)))
            throw new IOException("Invalid recovery hash.");
    }
}
