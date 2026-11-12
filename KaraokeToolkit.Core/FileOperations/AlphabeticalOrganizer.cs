using KaraokeToolkit.Core.Archives;
using System.Diagnostics;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Recovery;

namespace KaraokeToolkit.Core.FileOperations;

public enum AlphabeticalOrganizeScope { Files, Folders }

/// <summary>Moves files recursively or immediate child folders into A–Z/# folders.</summary>
public sealed class AlphabeticalOrganizer
{
    private static readonly StringComparer Paths = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public Task<ConversionResult> RunAsync(IEnumerable<string> inputs, AlphabeticalOrganizeScope scope,
        IProgress<ConversionProgress>? progress = null, CancellationToken token = default, bool ignoreLeadingThe = false)
        => Task.Run(() => Run(inputs, scope, progress, token, ignoreLeadingThe), token);

    private static ConversionResult Run(IEnumerable<string> inputs, AlphabeticalOrganizeScope scope, IProgress<ConversionProgress>? progress,
        CancellationToken token, bool ignoreLeadingThe)
    {
        if (!Enum.IsDefined(scope)) throw new ArgumentOutOfRangeException(nameof(scope));
        var timer = Stopwatch.StartNew();
        var selected = inputs.Select(p => Path.TrimEndingDirectorySeparator(Path.GetFullPath(p))).Distinct(Paths).ToArray();
        if (selected.Length == 0) throw new ArgumentException("Select at least one file or folder.");
        foreach (string path in selected)
        {
            RunSession.CheckPlainPath(path);
            if (!File.Exists(path) && !Directory.Exists(path)) throw new FileNotFoundException("Selected input is missing: " + path);
            if (path.Split(Path.DirectorySeparatorChar).Any(InputSelection.IsExcludedFolder))
                throw new IOException("Recovery and Damaged folders cannot be organized: " + path);
        }

        var roots = selected.Where(Directory.Exists).ToHashSet(Paths);
        if (scope == AlphabeticalOrganizeScope.Folders && selected.Any(File.Exists))
            throw new ArgumentException("Organize Folders Alphabetically requires folder selections.");
        var recovery = new RecoveryStore();
        foreach (string root in roots)
            if (recovery.Find(root, true).Count > 0)
                throw new IOException("Recover the interrupted conversion before organizing: " + root);
        var work = new Dictionary<string, HashSet<string>>(Paths);
        foreach (string root in roots)
        {
            if (scope == AlphabeticalOrganizeScope.Folders)
            {
                foreach (string entry in Directory.EnumerateDirectories(root))
                    if (!IsBucket(Path.GetFileName(entry)) && !IsExcluded(entry)) Add(root, entry);
            }
            else
            {
                Visit(root);
            }
        }
        foreach (string file in selected.Where(File.Exists))
        {
            string parent = Path.GetDirectoryName(file)!;
            if (roots.Contains(parent) || IsBucket(Path.GetFileName(parent))) continue;
            Add(parent, file);
            // Keep an explicitly selected audio/graphics track together with its matching mate.
            if (IsKaraokePairMember(file))
                foreach (string mate in Directory.EnumerateFiles(parent).Where(p =>
                    IsKaraokePairMember(p) &&
                    Path.GetFileNameWithoutExtension(p).Equals(Path.GetFileNameWithoutExtension(file), StringComparison.OrdinalIgnoreCase)))
                    Add(parent, mate);
        }

        var results = new List<ConversionFileResult>();
        int total = work.Values.Sum(x => x.Count), completed = 0;
        foreach (var (root, entries) in work.OrderBy(x => x.Key, Paths))
        {
            if (token.IsCancellationRequested) break;
            using var lease = FileTransaction.AcquireLease(root);
            // Existing interrupted conversion data must be handled before moving any folder.
            if (Directory.EnumerateDirectories(root, ".karaoke-job-*").Any(p => File.Exists(Path.Combine(p, "recovery.json"))))
                throw new IOException("Recover the interrupted conversion before organizing: " + root);
            var moves = entries.OrderBy(x => x, Paths).Select(source =>
                (Source: source, Destination: Path.Combine(root,
                    Bucket(Path.GetFileName(source), scope == AlphabeticalOrganizeScope.Files && ignoreLeadingThe), Path.GetFileName(source)))).ToArray();
            foreach (var group in moves.GroupBy(move => IsKaraokePairMember(move.Source)
                ? Path.GetFileNameWithoutExtension(move.Source) : move.Source, Paths))
            {
                if (token.IsCancellationRequested) break;
                var batch = group.ToArray();
                var committed = new List<(string Source, string Destination)>();
                try
                {
                    foreach (var move in batch)
                    {
                        RunSession.CheckPlainPath(move.Source);
                        RunSession.CheckPlainPath(Path.GetDirectoryName(move.Destination)!);
                    }
                    if (batch.Any(move => File.Exists(move.Destination) || Directory.Exists(move.Destination)))
                    {
                        foreach (var move in batch)
                            results.Add(new(move.Source, ConversionStatus.Skipped, [], "Destination for this item or its matching track already exists; source was left in place."));
                    }
                    else
                    {
                        foreach (var move in batch)
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(move.Destination)!);
                            if (Directory.Exists(move.Source)) Directory.Move(move.Source, move.Destination);
                            else File.Move(move.Source, move.Destination);
                            committed.Add(move);
                        }
                        foreach (var move in batch)
                            results.Add(new(move.Source, ConversionStatus.Succeeded, [move.Destination], "Moved to " + Path.GetFileName(Path.GetDirectoryName(move.Destination)) + "."));
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    foreach (var moved in committed.AsEnumerable().Reverse())
                    {
                        try
                        {
                            if (Directory.Exists(moved.Destination)) Directory.Move(moved.Destination, moved.Source);
                            else File.Move(moved.Destination, moved.Source);
                        }
                        catch (Exception rollback) when (rollback is IOException or UnauthorizedAccessException)
                        {
                            throw new IOException("A move failed and rollback could not restore " + moved.Source + ". Files need manual review.", rollback);
                        }
                    }
                    foreach (var move in batch)
                        results.Add(new(move.Source, ConversionStatus.Failed, [], e.Message));
                }
                completed += batch.Length;
                progress?.Report(new(100, total == 0 ? 100 : completed * 100 / total, total, completed,
                    $"Organized {completed} of {total} items.", timer.Elapsed.ToString(@"hh\:mm\:ss")));
            }
        }
        return new(results, token.IsCancellationRequested) { Found = total, Elapsed = timer.Elapsed };

        void Add(string root, string entry)
        {
            if (!work.TryGetValue(root, out var entries)) work[root] = entries = new(Paths);
            entries.Add(entry);
        }
        void Visit(string folder)
        {
            token.ThrowIfCancellationRequested();
            RunSession.CheckPlainPath(folder);
            foreach (string file in Directory.EnumerateFiles(folder))
                if (!IsExcluded(file)) Add(folder, file);
            foreach (string child in Directory.EnumerateDirectories(folder))
                if (!IsBucket(Path.GetFileName(child)) && !IsExcluded(child) &&
                    (File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                    Visit(child);
        }
    }

    private static bool IsExcluded(string path) => InputSelection.IsExcludedFolder(Path.GetFileName(path)) ||
        Path.GetFileName(path).Equals(".karaoke-run-lease", StringComparison.OrdinalIgnoreCase);
    private static bool IsBucket(string name) => name == "#" || name.Length == 1 && char.ToUpperInvariant(name[0]) is >= 'A' and <= 'Z';
    private static string Bucket(string name, bool ignoreLeadingThe)
    {
        if (ignoreLeadingThe && name.Length > 4 && name.StartsWith("The", StringComparison.OrdinalIgnoreCase) && char.IsWhiteSpace(name[3]))
        {
            string remainder = name[4..].TrimStart();
            if (remainder.Length > 0) name = remainder;
        }
        char c = char.ToUpperInvariant(name[0]);
        return c is >= 'A' and <= 'Z' ? c.ToString() : "#";
    }
    private static bool IsKaraokePairMember(string path) =>
        Path.GetExtension(path).Equals(".cdg", StringComparison.OrdinalIgnoreCase) ||
        KaraokeArchive.AudioExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
}
