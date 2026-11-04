using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Recovery;
using KaraokeToolkit.Core.Validation;
using System.Text.Json;

namespace KaraokeToolkit.Core.FileOperations;

public sealed record QuarantineResult(string Source, bool Succeeded, string? Manifest, IReadOnlyList<string> MovedFiles, string Message);
internal sealed record QuarantineEntry(string RelativePath, string Hash);
internal sealed class QuarantineManifest
{
    public int Version { get; set; } = 1;
    public string State { get; set; } = "Moving";
    public QuarantineEntry[] Entries { get; set; } = [];
}

/// <summary>Explicit, reversible moves. Each source group has a journal before the first rename.</summary>
public sealed class QuarantineService
{
    public IReadOnlyList<string> PreviewRestore(string manifestPath)
    {
        RunSession.CheckPlainPath(manifestPath);
        var manifest = JsonSerializer.Deserialize<QuarantineManifest>(File.ReadAllText(manifestPath)) ?? throw new IOException("Empty Damaged manifest.");
        var paths = Resolve(manifestPath, manifest);
        foreach (var item in paths)
        {
            RequireHash(item.Destination, item.Hash);
            if (File.Exists(item.Original) || Directory.Exists(item.Original)) throw new IOException("Restore destination already exists: " + item.Original);
        }
        return paths.Select(p => p.Original).ToArray();
    }
    public Task<IReadOnlyList<QuarantineResult>> MoveAsync(IEnumerable<ValidationFinding> selection, CancellationToken token = default)
        => Task.Run<IReadOnlyList<QuarantineResult>>(() =>
        {
            var results = new List<QuarantineResult>();
            foreach (var finding in selection.DistinctBy(f => f.Source, InputSelection.Paths))
            {
                token.ThrowIfCancellationRequested();
                if (!finding.CanMove)
                {
                    results.Add(new(finding.Source, false, null, [], "This result cannot be moved."));
                    continue;
                }
                string? manifestPath = null;
                try
                {
                    string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(finding.Root));
                    RunSession.CheckPlainPath(root);
                    var manifest = new QuarantineManifest { Entries = finding.Files.Select(f => new QuarantineEntry(Path.GetRelativePath(root, f.Path), f.Sha256)).ToArray() };
                    string directory = Path.Combine(root, "Damaged", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
                    manifestPath = Path.Combine(directory, "manifest.json");
                    var paths = Resolve(manifestPath, manifest);
                    using var leases = LockFolders(paths.Select(p => Path.GetDirectoryName(p.Original)!));
                    foreach (var item in paths)
                    {
                        RequireHash(item.Original, item.Hash);
                        if (File.Exists(item.Destination) || Directory.Exists(item.Destination))
                            throw new IOException("Damaged destination already exists.");
                    }
                    Directory.CreateDirectory(directory);
                    DurableJson.Write(manifestPath, manifest);
                    // Cancellation is observed between groups; never abandon half a pair deliberately.
                    try
                    {
                        foreach (var item in paths)
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(item.Destination)!);
                            File.Move(item.Original, item.Destination);
                        }
                        manifest.State = "Moved";
                        DurableJson.Write(manifestPath, manifest);
                    }
                    catch
                    {
                        RestoreCore(manifestPath, manifest, paths);
                        throw;
                    }
                    results.Add(new(finding.Source, true, manifestPath, paths.Select(p => p.Destination).ToArray(), "Moved to Damaged. Use the manifest to restore the group."));
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    results.Add(new(finding.Source, false, manifestPath, [], "Move stopped; existing files were not overwritten. " + error.Message));
                }
            }
            return results;
        }, CancellationToken.None);

    public Task RestoreAsync(string manifestPath) => Task.Run(() =>
    {
        RunSession.CheckPlainPath(manifestPath);
        var manifest = JsonSerializer.Deserialize<QuarantineManifest>(File.ReadAllText(manifestPath)) ?? throw new IOException("Empty Damaged manifest.");
        var paths = Resolve(manifestPath, manifest);
        using var leases = LockFolders(paths.Select(p => Path.GetDirectoryName(p.Original)!));
        RestoreCore(manifestPath, manifest, paths);
    });

    private static void RestoreCore(string path, QuarantineManifest manifest, MovePath[] paths)
    {
        // Check the whole pair first. A user-created replacement is never overwritten.
        foreach (var item in paths)
        {
            if (File.Exists(item.Destination))
            {
                RequireHash(item.Destination, item.Hash);
                if (File.Exists(item.Original) || Directory.Exists(item.Original))
                    throw new IOException("Restore destination already exists: " + item.Original);
            }
            else
                RequireHash(item.Original, item.Hash); // Already restored, or not moved when interrupted.
        }
        manifest.State = "Restoring";
        DurableJson.Write(path, manifest);
        foreach (var item in paths)
            if (File.Exists(item.Destination))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(item.Original)!);
                File.Move(item.Destination, item.Original);
            }
        manifest.State = "Restored";
        DurableJson.Write(path, manifest);
    }

    private sealed record MovePath(string Original, string Destination, string Hash);
    private static MovePath[] Resolve(string manifestPath, QuarantineManifest manifest)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(manifestPath))!;
        string damaged = Path.GetDirectoryName(directory)!;
        string root = Path.GetDirectoryName(damaged)!;
        if (Path.GetFileName(manifestPath) != "manifest.json" || !Path.GetFileName(damaged).Equals("Damaged", StringComparison.OrdinalIgnoreCase) ||
            manifest.Version != 1 || manifest.Entries == null || manifest.Entries.Length == 0)
            throw new IOException("Invalid Damaged manifest.");
        RunSession.CheckPlainPath(directory);
        var result = new List<MovePath>();
        foreach (var entry in manifest.Entries)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.RelativePath) || Path.IsPathRooted(entry.RelativePath) || entry.RelativePath.Contains(':') ||
                entry.RelativePath.Replace('\\', '/').Split('/').Any(p => p is ".." or "." || InputSelection.IsExcludedFolder(p)) ||
                entry.Hash == null || entry.Hash.Length != 64 || entry.Hash.Any(c => !Uri.IsHexDigit(c)))
                throw new IOException("Unsafe Damaged manifest entry.");
            string source = Path.GetFullPath(Path.Combine(root, entry.RelativePath));
            string target = Path.GetFullPath(Path.Combine(directory, "files", entry.RelativePath));
            if (!RunSession.Within(root, source) || !RunSession.Within(directory, target))
                throw new IOException("Damaged path is outside its source root.");
            RunSession.CheckPlainPath(source);
            RunSession.CheckPlainPath(target);
            result.Add(new(source, target, entry.Hash));
        }
        if (result.Select(p => p.Original).Distinct(InputSelection.Paths).Count() != result.Count)
            throw new IOException("Duplicate Damaged manifest entry.");
        return result.ToArray();
    }
    private static void RequireHash(string path, string hash)
    {
        RunSession.CheckPlainPath(path);
        if (!File.Exists(path) || FileTransaction.Hash(path) != hash)
            throw new IOException("File is missing or changed since validation: " + path);
    }
    private static IDisposable LockFolders(IEnumerable<string> folders)
    {
        var locks = new List<FileStream>();
        try
        {
            foreach (string folder in folders.Distinct(InputSelection.Paths).Order(InputSelection.Paths))
            {
                foreach (string job in Directory.Exists(folder) ? Directory.GetDirectories(folder, ".karaoke-job-*") : [])
                    if (File.Exists(Path.Combine(job, "recovery.json")) || File.Exists(Path.Combine(job, "run.json")))
                        throw new IOException("Finish recovery before moving files from " + folder);
                locks.Add(FileTransaction.AcquireLease(folder));
            }
            return new LeaseSet(locks);
        }
        catch { foreach (var lease in locks) lease.Dispose(); throw; }
    }
    private sealed class LeaseSet(List<FileStream> leases) : IDisposable
    {
        public void Dispose()
        {
            foreach (var lease in leases)
                lease.Dispose();
        }
    }
}
