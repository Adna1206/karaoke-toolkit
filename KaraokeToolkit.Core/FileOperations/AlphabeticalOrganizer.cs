using KaraokeToolkit.Core.Archives;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Execution;
using KaraokeToolkit.Core.Recovery;

namespace KaraokeToolkit.Core.FileOperations;

public enum AlphabeticalOrganizeScope { Files, Folders }

/// <summary>Moves files recursively or immediate child folders into A–Z/# folders.</summary>
public sealed class AlphabeticalOrganizer(RecoveryStore? recovery = null)
{
    private static readonly StringComparer Paths = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private readonly RecoveryStore recovery = recovery ?? new();

    public Task<ConversionResult> RunAsync(IEnumerable<string> inputs, AlphabeticalOrganizeScope scope,
        IProgress<ConversionProgress>? progress = null, CancellationToken token = default, bool ignoreLeadingThe = false,
        RecoveryVerificationMode verificationMode = OperationCapabilities.DefaultFileMoveRecoveryVerification, bool saveResumableRun = true)
        => Task.Run(() => Run(inputs, scope, progress, token, ignoreLeadingThe, verificationMode: verificationMode, saveResumableRun: saveResumableRun), token);

    public Task<ConversionResult> PreviewAsync(IEnumerable<string> inputs, AlphabeticalOrganizeScope scope,
        CancellationToken token = default, bool ignoreLeadingThe = false)
        => Task.Run(() => Run(inputs, scope, null, token, ignoreLeadingThe, true), token);

    public Task<ConversionResult> ApplyPreviewAsync(IEnumerable<string> inputs, AlphabeticalOrganizeScope scope,
        ConversionResult approved, IProgress<ConversionProgress>? progress = null, CancellationToken token = default,
        bool ignoreLeadingThe = false, RecoveryVerificationMode verificationMode = OperationCapabilities.DefaultFileMoveRecoveryVerification,
        bool saveResumableRun = true)
        => Task.Run(() => Run(inputs, scope, progress, token, ignoreLeadingThe, verificationMode: verificationMode,
            saveResumableRun: saveResumableRun, approved: approved), token);

    private ConversionResult Run(IEnumerable<string> inputs, AlphabeticalOrganizeScope scope, IProgress<ConversionProgress>? progress,
        CancellationToken token, bool ignoreLeadingThe, bool preview = false,
        RecoveryVerificationMode verificationMode = OperationCapabilities.DefaultFileMoveRecoveryVerification,
        bool saveResumableRun = true, ConversionResult? approved = null)
    {
        if (!Enum.IsDefined(scope)) throw new ArgumentOutOfRangeException(nameof(scope));

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
            throw new ArgumentException("Select folders to group their immediate subfolders alphabetically.");
        if (scope == AlphabeticalOrganizeScope.Folders && roots.Any(root => roots.Any(parent =>
            !Paths.Equals(parent, root) && WorkflowSession.IsWithin(parent, root))))
            throw new ArgumentException("Select non-overlapping folders when organizing child folders.");
        var moveRoots = WorkflowSession.RootsFor(selected);
        IEnumerable<string> recoveryRoots = approved != null || preview ? moveRoots : roots;
        foreach (string root in recoveryRoots)
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

        var groups = work.OrderBy(pair => pair.Key, Paths).SelectMany(pair =>
            pair.Value.Order(Paths).GroupBy(source => IsKaraokePairMember(source) ? Path.GetFileNameWithoutExtension(source) : source, Paths)
                .Select(group => group.Select(source => (Source: source, Target: Path.Combine(pair.Key,
                    Bucket(Path.GetFileName(source), scope == AlphabeticalOrganizeScope.Files && ignoreLeadingThe),
                    Path.GetFileName(source)))).ToArray())).ToArray();

        string?[]? issues = null;
        if (preview || approved != null)
        {
            var duplicates = groups.SelectMany(group => group).CountBy(move => move.Target, Paths)
                .Where(entry => entry.Value > 1).Select(entry => entry.Key).ToHashSet(Paths);
            issues = groups.Select(group => FindIssue(group, duplicates)).ToArray();
            var rows = groups.SelectMany((group, index) => group.Select(move => new ConversionFileResult(move.Source,
                issues[index] == null ? ConversionStatus.Succeeded : ConversionStatus.Failed,
                [move.Target], issues[index] ?? "Planned move."))).ToArray();
            if (preview) return new(rows, false) { Found = rows.Length };
            if (approved!.Files.Count != rows.Length || !approved.Files.Zip(rows).All(pair =>
                Paths.Equals(pair.First.Source, pair.Second.Source) && pair.First.Status == pair.Second.Status &&
                pair.First.Message == pair.Second.Message && pair.First.Outputs.SequenceEqual(pair.Second.Outputs, Paths)))
                throw new IOException("The alphabetical grouping plan changed. Refresh the preview before moving files.");
        }
        if (token.IsCancellationRequested) return new([], true);
        progress?.Report(new(0, 0, 0, 0, "Preparing recovery for file moves…", "00:00:00") { IsIndeterminate = true });
        var mode = WorkflowSession.Current.Value?.Data.VerificationMode ?? verificationMode;
        var plan = new List<WorkflowMoveGroup>();
        for (int index = 0; index < groups.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            var group = groups[index];
            string? issue = issues?[index];
            plan.Add(issue == null
                ? new() { Items = group.Select(move => WorkflowMoves.Move(move.Source, move.Target, mode)).ToArray() }
                : new() { Items = group.Select(move => new WorkflowMove(move.Source, null, "")).ToArray(), SkipReason = issue });
        }
        return WorkflowMoves.RunStandaloneOrRecipe(recovery, moveRoots,
            scope == AlphabeticalOrganizeScope.Files ? OperationId.OrganizeFiles : OperationId.OrganizeFolders,
            plan, mode, saveResumableRun, progress, token);

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

    private static string? FindIssue((string Source, string Target)[] group, HashSet<string> duplicates)
    {
        foreach (var (source, target) in group)
        {
            if (duplicates.Contains(target)) return "Multiple inputs target the same path.";
            if (WorkflowMoves.Exists(target) || File.Exists(Path.GetDirectoryName(target)))
                return "A destination is occupied. The group stays in place.";
            try
            {
                RunSession.CheckPlainPath(source);
                RunSession.CheckPlainPath(target);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                return error.Message;
            }
        }
        return null;
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
