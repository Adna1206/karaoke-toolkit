using KaraokeToolkit.Core.DiscRipping;
using KaraokeToolkit.Core.Conversion;
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
    private bool publicationResolved;
    private bool disposed;
    private readonly FileStream folderLease;
    internal string Workspace
    {
        get;
    }
    internal RecoveryVerificationMode VerificationMode { get; }
    internal FileTransaction(string folder, RecoveryVerificationMode verificationMode = RecoveryVerificationMode.Sha256)
    {
        if (!Enum.IsDefined(verificationMode)) throw new ArgumentOutOfRangeException(nameof(verificationMode));
        VerificationMode = verificationMode;
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
        string lockName = RecoveryFingerprint.HashBytes(System.Text.Encoding.UTF8.GetBytes(key)) + ".lock";
        return new FileStream(Path.Combine(lockDirectory, lockName), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    internal string Fingerprint(string path) => RecoveryFingerprint.Hash(path, VerificationMode);
    internal static string Hash(string path) => RecoveryFingerprint.Hash(path);
    /// <summary>Copies an input into staging and records its verified hash for publication checks.</summary>
    /// <returns>The staged path; the original remains unchanged.</returns>
    internal async Task<string> CopyInputAsync(string path, CancellationToken token, string? expectedFingerprint = null)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Symbolic-link inputs are not supported: " + path);
        string destination = Path.Combine(Workspace, Path.GetFileName(path));
        string hash = await FileStaging.CopyInputAsync(path, destination, VerificationMode, token);
        if (expectedFingerprint != null && hash != expectedFingerprint)
            throw new IOException("Conversion input changed since the batch started: " + path);
        if (!File.Exists(destination) || hash != await RecoveryFingerprint.HashFileAsync(destination, VerificationMode, token))
            throw new IOException("Staged input failed verification: " + path);
        fingerprints.Add(path, hash);
        return destination;
    }

    /// <summary>Protects an overwrite decision against files changing while a prompt is open.</summary>
    internal void ObserveTargets(IReadOnlyList<string> outputs)
    {
        foreach (string output in outputs)
        {
            string target = Path.Combine(folder, Path.GetFileName(output));
            observedTargets[target] = File.Exists(target) ? Fingerprint(target) : null;
        }
    }

    internal TransactionObservation Observation => new(new(fingerprints, fingerprints.Comparer),
        new(observedTargets, observedTargets.Comparer));

    internal void RestoreObservation(TransactionObservation observation)
    {
        foreach (var (path, hash) in observation.Inputs) fingerprints.Add(path, hash);
        foreach (var (path, hash) in observation.Targets) observedTargets.Add(path, hash);
    }
    /// <summary>Publishes staged outputs and removes selected sources as one recoverable file group.</summary>
    /// <remarks>Ask must be resolved beforehand. Inputs and observed targets are checked before moves;
    /// failed publication attempts rollback. Cancellation is checked before publication, not between moves.
    /// Durable owner receipts precede cleanup; this is not a power-loss-atomic filesystem operation.</remarks>
    /// <returns>Published destination paths.</returns>
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
        // Reuse only fingerprints read at this commit boundary, never the observations from an earlier review.
        var checkedFiles = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        string? CheckFingerprint(string path)
        {
            if (!checkedFiles.TryGetValue(path, out string? current))
                checkedFiles[path] = current = File.Exists(path) ? Fingerprint(path) : null;
            return current;
        }
        foreach (var pair in fingerprints)
            if (CheckFingerprint(pair.Key) != pair.Value)
                throw new IOException("Source changed during conversion: " + pair.Key);
        foreach (var pair in observedTargets)
        {
            string? current = CheckFingerprint(pair.Key);
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
            VerificationMode = VerificationMode.ToString(),
            Outputs = outputs.Select(p => new RecoveryOutput(Path.GetFileName(p), Fingerprint(p))).ToArray(),
            Backups = oldFiles.Select((p, i) => new RecoveryBackup(Path.GetFileName(p), i.ToString(),
                CheckFingerprint(p) ?? throw new IOException("A replacement target changed before backup: " + p))).ToArray()
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
                if (!File.Exists(targets[i]) || Fingerprint(targets[i]) != journal.Outputs[i].Hash)
                    throw new IOException("Published output failed verification: " + targets[i]);
            }
            journal.Phase = "Committed";
            DurableJson.Write(Path.Combine(Workspace, "recovery.json"), journal);
            committed = true;
            // A durable receipt precedes deletion of the journal and backups in Dispose.
            RunSession.Current.Value?.Committed(targets);
            WorkflowSession.Current.Value?.Published(targets, removeSources);
            publicationResolved = true;
            return targets;
        }
        catch (Exception original)
        {
            if (committed)
            {
                retain = true;
                throw;
            }
            if (!RecoveryFingerprint.IsContentVerified(VerificationMode))
            {
                retain = true;
                throw new IOException("Publication was interrupted without content fingerprints. Manual recovery is required: " + Workspace, original);
            }
            try
            {
                FileTransactionRecovery.Rollback(Workspace, journal);
                publicationResolved = true;
            }
            catch (Exception rollback)
            {
                retain = true;
                throw new IOException($"Commit and rollback failed. Recovery files retained at {Workspace}", new AggregateException(original, rollback));
            }
            throw;
        }
    }
    /// <summary>Removes resolved staging and releases the folder lease, retaining unresolved recovery files.</summary>
    /// <exception cref="IOException">Publication requires recovery or disposable files could not be cleaned.
    /// Already published outputs may still exist when cleanup fails.</exception>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try
        {
            if (retain) return;
            if (!publicationResolved && File.Exists(Path.Combine(Workspace, "recovery.json")))
                throw new IOException("Recovery required before cleaning transaction files: " + Workspace);
            RecoveryWorkspaceCleanup.Delete(Workspace);
        }
        finally { folderLease.Dispose(); }
    }
}
internal sealed class OutputSkippedException : IOException;

internal sealed record TransactionObservation(Dictionary<string, string> Inputs, Dictionary<string, string?> Targets);




