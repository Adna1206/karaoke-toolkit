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
        RecoveryVerificationMode verificationMode = OperationCapabilities.DefaultFileMoveRecoveryVerification, bool saveResumableRun = true,
        FileCheckTiming timing = OperationCapabilities.DefaultFileMoveCheckTiming)
        => Task.Run(() => Run(inputs, scope, progress, token, ignoreLeadingThe, verificationMode: verificationMode,
            saveResumableRun: saveResumableRun, timing: timing), token);

    public Task<ConversionResult> PreviewAsync(IEnumerable<string> inputs, AlphabeticalOrganizeScope scope,
        CancellationToken token = default, bool ignoreLeadingThe = false)
        => Task.Run(() => Run(inputs, scope, null, token, ignoreLeadingThe, true), token);

    public sealed class PreviewSnapshot
    {
        private readonly ConversionResult? files, filesIgnoringArticle, folders;
        private readonly string? fileError, folderError;

        internal PreviewSnapshot(ConversionResult? files, ConversionResult? filesIgnoringArticle, ConversionResult? folders,
            string? fileError, string? folderError)
        {
            this.files = files;
            this.filesIgnoringArticle = filesIgnoringArticle;
            this.folders = folders;
            this.fileError = fileError;
            this.folderError = folderError;
        }

        public ConversionResult Plan(AlphabeticalOrganizeScope scope, bool ignoreLeadingThe) => scope switch
        {
            AlphabeticalOrganizeScope.Files => (ignoreLeadingThe ? filesIgnoringArticle : files) ?? throw new IOException(fileError),
            AlphabeticalOrganizeScope.Folders => folders ?? throw new IOException(folderError),
            _ => throw new ArgumentOutOfRangeException(nameof(scope))
        };
    }

    public Task<PreviewSnapshot> ReadPreviewAsync(IReadOnlyList<string> inputs, CancellationToken token) => Task.Run(() =>
    {
        var listings = new Dictionary<string, FileSystemInfo[]>(Paths);
        var checkedRecoveryRoots = new HashSet<string>(Paths);
        ConversionResult? files = null, filesIgnoringArticle = null, folders = null;
        string? fileError = null, folderError = null;
        try
        {
            var work = Collect(inputs, AlphabeticalOrganizeScope.Files, token, checkAllRecoveryRoots: true, ReadDirectory, out _, checkedRecoveryRoots);
            files = Preview(Groups(work, ignoreLeadingThe: false));
            filesIgnoringArticle = Preview(Groups(work, ignoreLeadingThe: true));
        }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException)
        {
            fileError = error.Message;
        }
        try
        {
            var work = Collect(inputs, AlphabeticalOrganizeScope.Folders, token, checkAllRecoveryRoots: true, ReadDirectory, out _, checkedRecoveryRoots);
            folders = Preview(Groups(work, ignoreLeadingThe: false));
        }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException)
        {
            folderError = error.Message;
        }
        return new PreviewSnapshot(files, filesIgnoringArticle, folders, fileError, folderError);

        FileSystemInfo[] ReadDirectory(string folder)
        {
            if (!listings.TryGetValue(folder, out var entries))
                listings[folder] = entries = ReadEntries(folder, token);
            return entries;
        }
    }, token);

    public Task<ConversionResult> ApplyPreviewAsync(IEnumerable<string> inputs, AlphabeticalOrganizeScope scope,
        ConversionResult approved, IProgress<ConversionProgress>? progress = null, CancellationToken token = default,
        bool ignoreLeadingThe = false, RecoveryVerificationMode verificationMode = OperationCapabilities.DefaultFileMoveRecoveryVerification,
        bool saveResumableRun = true, FileCheckTiming timing = OperationCapabilities.DefaultFileMoveCheckTiming)
        => Task.Run(() => ApplyApproved(inputs, scope, approved, progress, token, ignoreLeadingThe, verificationMode,
            saveResumableRun, timing), token);

    private ConversionResult ApplyApproved(IEnumerable<string> inputs, AlphabeticalOrganizeScope scope, ConversionResult approved,
        IProgress<ConversionProgress>? progress, CancellationToken token, bool ignoreLeadingThe,
        RecoveryVerificationMode mode, bool saveResumableRun, FileCheckTiming timing)
    {
        if (!Enum.IsDefined(scope)) throw new ArgumentOutOfRangeException(nameof(scope));
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        timing = WorkflowMoves.TimingForStandalone(timing);
        bool files = scope == AlphabeticalOrganizeScope.Files;
        var selected = inputs.Select(path => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path))).ToArray();
        var sources = new HashSet<string>(Paths);
        foreach (var row in approved.Files)
        {
            string? parent = Path.GetDirectoryName(row.Source);
            if (!WorkflowMoves.IsNormalizedPath(row.Source) || parent == null || !sources.Add(row.Source) ||
                !(files ? WorkflowMoves.IsApprovedSource(selected, row.Source, includeCompanions: true) : selected.Contains(parent, Paths)) ||
                row.Status is not (ConversionStatus.Succeeded or ConversionStatus.Failed) || row.Outputs.Count != 1 ||
                !Paths.Equals(row.Outputs[0], Path.Combine(parent, Bucket(Path.GetFileName(row.Source), files && ignoreLeadingThe), Path.GetFileName(row.Source))))
                throw new ArgumentException("The approved alphabetical grouping plan contains an invalid source or destination.");
        }
        var ready = approved.Files.Where(row => row.Status == ConversionStatus.Succeeded).ToArray();
        WorkflowMoves.CheckApprovedRecovery(recovery, ready.SelectMany(row =>
            new[] { Path.GetDirectoryName(row.Source)!, Path.GetDirectoryName(row.Outputs[0])! }), token);
        progress?.Report(new(0, 0, approved.Files.Count, 0, "Preparing recovery for file moves…", "00:00:00") { IsIndeterminate = true });
        var groups = approved.Files.GroupBy(row => Path.GetDirectoryName(row.Source), Paths).SelectMany(folder =>
            folder.GroupBy(row => IsKaraokePairMember(row.Source) ? Path.GetFileNameWithoutExtension(row.Source) : row.Source, Paths))
            .Select(group =>
            {
                token.ThrowIfCancellationRequested();
                return group.FirstOrDefault(row => row.Status != ConversionStatus.Succeeded) is { } issue
                    ? new WorkflowMoveGroup { Items = group.Select(row => new WorkflowMove(row.Source, null, "")).ToArray(), SkipReason = issue.Message }
                    : WorkflowMoves.ApprovedGroup(group.Select(row => new WorkflowMove(row.Source, row.Outputs[0], "", !files)), mode, timing);
            }).ToList();
        return WorkflowMoves.RunApproved(recovery, files ? OperationId.OrganizeFiles : OperationId.OrganizeFolders, groups, mode,
            saveResumableRun, progress, token, timing);
    }

    private ConversionResult Run(IEnumerable<string> inputs, AlphabeticalOrganizeScope scope, IProgress<ConversionProgress>? progress,
        CancellationToken token, bool ignoreLeadingThe, bool preview = false,
        RecoveryVerificationMode verificationMode = OperationCapabilities.DefaultFileMoveRecoveryVerification,
        bool saveResumableRun = true, FileCheckTiming timing = OperationCapabilities.DefaultFileMoveCheckTiming)
    {
        if (!Enum.IsDefined(scope)) throw new ArgumentOutOfRangeException(nameof(scope));

        var work = Collect(inputs, scope, token, preview, folder => ReadEntries(folder, token), out var moveRoots);
        var groups = Groups(work, scope == AlphabeticalOrganizeScope.Files && ignoreLeadingThe);
        if (preview) return Preview(groups);
        if (token.IsCancellationRequested) return new([], true);
        progress?.Report(new(0, 0, 0, 0, "Preparing recovery for file moves…", "00:00:00") { IsIndeterminate = true });
        var mode = WorkflowSession.Current.Value?.Data.VerificationMode ?? verificationMode;
        timing = WorkflowMoves.TimingForStandalone(timing);
        var plan = groups.Select(group =>
        {
            token.ThrowIfCancellationRequested();
            return new WorkflowMoveGroup { Items = group.Select(move => WorkflowMoves.Move(move.Source, move.Target, mode, timing)).ToArray() };
        }).ToList();
        return WorkflowMoves.RunStandaloneOrRecipe(recovery, moveRoots,
            scope == AlphabeticalOrganizeScope.Files ? OperationId.OrganizeFiles : OperationId.OrganizeFolders,
            plan, mode, saveResumableRun, progress, token, timing);
    }

    private Dictionary<string, HashSet<string>> Collect(IEnumerable<string> inputs, AlphabeticalOrganizeScope scope,
        CancellationToken token, bool checkAllRecoveryRoots, Func<string, FileSystemInfo[]> readDirectory, out string[] moveRoots,
        HashSet<string>? checkedRecoveryRoots = null)
    {
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
        moveRoots = WorkflowSession.RootsFor(selected);
        IEnumerable<string> recoveryRoots = checkAllRecoveryRoots ? moveRoots : roots;
        foreach (string root in recoveryRoots)
        {
            if (checkedRecoveryRoots?.Contains(root) == true) continue;
            if (recovery.Find(root, true).Count > 0)
                throw new IOException("Recover the interrupted conversion before organizing: " + root);
            checkedRecoveryRoots?.Add(root);
        }
        var work = new Dictionary<string, HashSet<string>>(Paths);
        foreach (string root in roots)
        {
            if (scope == AlphabeticalOrganizeScope.Folders)
            {
                foreach (string entry in readDirectory(root).OfType<DirectoryInfo>().Select(entry => entry.FullName))
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
                foreach (string mate in readDirectory(parent).OfType<FileInfo>().Select(entry => entry.FullName).Where(p =>
                    IsKaraokePairMember(p) &&
                    Path.GetFileNameWithoutExtension(p).Equals(Path.GetFileNameWithoutExtension(file), StringComparison.OrdinalIgnoreCase)))
                    Add(parent, mate);
        }

        return work;

        void Add(string root, string entry)
        {
            if (!work.TryGetValue(root, out var entries)) work[root] = entries = new(Paths);
            entries.Add(entry);
        }
        void Visit(string folder)
        {
            token.ThrowIfCancellationRequested();
            RunSession.CheckPlainPath(folder);
            var entries = readDirectory(folder);
            foreach (var file in entries.OfType<FileInfo>())
                if (!IsExcluded(file.FullName)) Add(folder, file.FullName);
            foreach (var child in entries.OfType<DirectoryInfo>())
                if (!IsBucket(child.Name) && !IsExcluded(child.FullName) && (child.Attributes & FileAttributes.ReparsePoint) == 0)
                    Visit(child.FullName);
        }
    }

    private static FileSystemInfo[] ReadEntries(string folder, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return new DirectoryInfo(folder).GetFileSystemInfos();
    }

    private static (string Source, string Target)[][] Groups(Dictionary<string, HashSet<string>> work, bool ignoreLeadingThe) =>
        work.OrderBy(pair => pair.Key, Paths).SelectMany(pair =>
            pair.Value.Order(Paths).GroupBy(source => IsKaraokePairMember(source) ? Path.GetFileNameWithoutExtension(source) : source, Paths)
                .Select(group => group.Select(source => (Source: source, Target: Path.Combine(pair.Key,
                    Bucket(Path.GetFileName(source), ignoreLeadingThe), Path.GetFileName(source)))).ToArray())).ToArray();

    private static ConversionResult Preview((string Source, string Target)[][] groups)
    {
        var duplicates = groups.SelectMany(group => group).CountBy(move => move.Target, Paths)
            .Where(entry => entry.Value > 1).Select(entry => entry.Key).ToHashSet(Paths);
        var foundIssues = groups.Select(group => FindIssue(group, duplicates)).ToArray();
        var rows = groups.SelectMany((group, index) => group.Select(move => new ConversionFileResult(move.Source,
            foundIssues[index] == null ? ConversionStatus.Succeeded : ConversionStatus.Failed,
            [move.Target], foundIssues[index] ?? "Planned move."))).ToArray();
        return new(rows, false) { Found = rows.Length };
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
