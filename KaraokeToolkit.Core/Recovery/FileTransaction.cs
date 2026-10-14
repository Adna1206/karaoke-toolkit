using KaraokeToolkit.Core.DiscRipping;
using KaraokeToolkit.Core.Conversion;
using System.Security.Cryptography;
using System.Text.Json;
namespace KaraokeToolkit.Core.Recovery;

/// <summary>Stages files on the destination volume and rolls back a failed group commit.</summary>
/// <remarks>A multi-file commit is not power-loss atomic. A journal and backups are
/// retained if rollback fails. Batch checkpoints precede cleanup so crash recovery can
/// distinguish completed publication from a group that still needs finishing.</remarks>
internal sealed class FileTransaction : IDisposable
{
    private readonly string folder;
    private readonly Dictionary<string, string> fingerprints = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string?> observedTargets = new(StringComparer.OrdinalIgnoreCase);
    private bool retain;
    private readonly FileStream folderLease;
    internal string Workspace
    {
        get;
    }
    internal FileTransaction(string folder)
    {
        this.folder = Path.GetFullPath(folder);
        RunSession.CheckPlainPath(this.folder);
        folderLease = AcquireLease(this.folder);
        Workspace = Path.Combine(this.folder, ".karaoke-job-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (string previous in Directory.GetDirectories(this.folder, ".karaoke-job-*"))
                if (File.Exists(Path.Combine(previous, "recovery.json")))
                    throw new IOException("Recovery required before writing to this folder. Retained files: " + previous);
            Directory.CreateDirectory(Workspace);
            RunSession.Current.Value?.Attach(Workspace);
            WorkflowSession.Current.Value?.Attach(Workspace);
        }
        catch { folderLease.Dispose(); throw; }
    }
    internal static FileStream AcquireLease(string folder)
    {
        string lockDirectory = Path.Combine(Path.GetTempPath(), "KaraokeToolkitLocks");
        Directory.CreateDirectory(lockDirectory);
        string key = OperatingSystem.IsWindows() ? Path.GetFullPath(folder).ToUpperInvariant() : Path.GetFullPath(folder);
        string lockName = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key))) + ".lock";
        return new FileStream(Path.Combine(lockDirectory, lockName), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    internal static string Hash(string path)
    {
        using var file = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(file));
    }
    internal async Task<string> CopyInputAsync(string path, CancellationToken token)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Symbolic-link inputs are not supported: " + path);
        string destination = Path.Combine(Workspace, Path.GetFileName(path));
        await using (var input = File.OpenRead(path))
        await using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write))
            await input.CopyToAsync(output, token);
        string hash = Hash(destination);
        if (hash != Hash(path))
            throw new IOException("Input changed while being copied: " + path);
        fingerprints.Add(path, hash);
        return destination;
    }

    /// <summary>Protects an overwrite decision against files changing while a prompt is open.</summary>
    internal void ObserveTargets(IReadOnlyList<string> outputs)
    {
        foreach (string output in outputs)
        {
            string target = Path.Combine(folder, Path.GetFileName(output));
            observedTargets[target] = File.Exists(target) ? Hash(target) : null;
        }
    }
    internal IReadOnlyList<string> Commit(IReadOnlyList<string> outputs, IReadOnlyList<string> removeSources,
        OverwritePolicy policy, CancellationToken token)
    {
        if (policy == OverwritePolicy.Ask)
            throw new InvalidOperationException("Resolve the overwrite decision before committing.");
        var targets = outputs.Select(p => Path.Combine(folder, Path.GetFileName(p))).ToArray();
        if (targets.Distinct(StringComparer.OrdinalIgnoreCase).Count() != targets.Length)
            throw new IOException("Multiple outputs have the same destination.");
        foreach (string path in outputs)
            if (Path.GetDirectoryName(Path.GetFullPath(path)) != Workspace || !File.Exists(path))
                throw new IOException("Output is not a staged file: " + path);
        foreach (string path in removeSources)
            if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), folder, StringComparison.OrdinalIgnoreCase))
                throw new IOException("A source selected for removal is outside the transaction folder.");
        foreach (var pair in fingerprints)
            if (!File.Exists(pair.Key) || Hash(pair.Key) != pair.Value)
                throw new IOException("Source changed during conversion: " + pair.Key);
        foreach (var pair in observedTargets)
        {
            string? current = File.Exists(pair.Key) ? Hash(pair.Key) : null;
            if (current != pair.Value)
                throw new IOException("Output changed while waiting for publication: " + pair.Key);
        }
        bool collision = targets.Any(p => File.Exists(p) && !removeSources.Contains(p, StringComparer.OrdinalIgnoreCase));
        if (collision && policy == OverwritePolicy.Skip)
            throw new OutputSkippedException();
        if (collision && policy == OverwritePolicy.Fail)
            throw new IOException("Output already exists; select Replace or Skip in Options.");
        foreach (string target in targets)
            if (Directory.Exists(target) || (File.Exists(target) && (File.GetAttributes(target) & (FileAttributes.ReadOnly | FileAttributes.ReparsePoint)) != 0))
                throw new IOException("Output is a directory, read-only file or link: " + target);
        token.ThrowIfCancellationRequested();
        string backups = Path.Combine(Workspace, "backups");
        Directory.CreateDirectory(backups);
        var oldFiles = targets.Concat(removeSources).Distinct(StringComparer.OrdinalIgnoreCase).Where(File.Exists).ToArray();
        var journal = new TransactionJournal
        {
            Outputs = outputs.Select(p => new RecoveryOutput(Path.GetFileName(p), Hash(p))).ToArray(),
            Backups = oldFiles.Select((p, i) => new RecoveryBackup(Path.GetFileName(p), i.ToString(), Hash(p))).ToArray()
        };
        foreach (string output in outputs)
            using (var file = new FileStream(output, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
                file.Flush(true);
        DurableJson.Write(Path.Combine(Workspace, "recovery.json"), journal);
        bool committed = false;
        try
        {
            foreach (var old in journal.Backups)
                File.Move(Path.Combine(folder, old.Name), Path.Combine(backups, old.Slot));
            for (int i = 0; i < outputs.Count; i++)
            {
                File.Move(outputs[i], targets[i]);
                if (Hash(targets[i]) != journal.Outputs[i].Hash)
                    throw new IOException("Published output failed verification: " + targets[i]);
            }
            journal.Phase = "Committed";
            DurableJson.Write(Path.Combine(Workspace, "recovery.json"), journal);
            committed = true;
            // A durable receipt precedes deletion of the journal and backups in Dispose.
            RunSession.Current.Value?.Committed(targets);
            WorkflowSession.Current.Value?.Published(targets, removeSources);
            return targets;
        }
        catch (Exception original)
        {
            if (committed)
            {
                retain = true;
                throw;
            }
            try
            {
                FileTransactionRecovery.Rollback(Workspace, journal);
            }
            catch (Exception rollback)
            {
                retain = true;
                throw new IOException($"Commit and rollback failed. Recovery files retained at {Workspace}", new AggregateException(original, rollback));
            }
            throw;
        }
    }
    public void Dispose()
    {
        if (retain)
        {
            folderLease.Dispose();
            return;
        }
        try
        {
            Directory.Delete(Workspace, recursive: true);
        }
        catch (IOException) { /* Retain journal/backups rather than hide a successful conversion. */ }
        catch (UnauthorizedAccessException) { }
        finally { folderLease.Dispose(); }
    }
}
internal sealed class OutputSkippedException : IOException;




