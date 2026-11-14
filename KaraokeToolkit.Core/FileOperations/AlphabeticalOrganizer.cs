using KaraokeToolkit.Core.Archives;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Execution;
using KaraokeToolkit.Core.Recovery;

namespace KaraokeToolkit.Core.FileOperations;

public enum AlphabeticalOrganizeScope { Files, Folders }

/// <summary>Moves files recursively or immediate child folders into A–Z/# folders.</summary>
public sealed class AlphabeticalOrganizer
{
    private static readonly StringComparer Paths = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public Task<ConversionResult> RunAsync(IEnumerable<string> inputs, AlphabeticalOrganizeScope scope,
        IProgress<ConversionProgress>? progress = null, CancellationToken token = default, bool ignoreLeadingThe = false, RecoveryVerificationMode verificationMode = RecoveryVerificationMode.Sha256, bool saveResumableRun = true)
        => Task.Run(() => Run(inputs, scope, progress, token, ignoreLeadingThe, verificationMode: verificationMode, saveResumableRun: saveResumableRun), token);

    public Task<ConversionResult> PreviewAsync(IEnumerable<string> inputs, AlphabeticalOrganizeScope scope,
        CancellationToken token = default, bool ignoreLeadingThe = false)
        => Task.Run(() => Run(inputs, scope, null, token, ignoreLeadingThe, true), token);

    private static ConversionResult Run(IEnumerable<string> inputs, AlphabeticalOrganizeScope scope, IProgress<ConversionProgress>? progress,
        CancellationToken token, bool ignoreLeadingThe, bool preview = false, RecoveryVerificationMode verificationMode = RecoveryVerificationMode.Sha256, bool saveResumableRun = true)
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

        if (preview)
        {
            var planned = work.SelectMany(pair => pair.Value.Select(source =>
            {
                string target = Path.Combine(pair.Key, Bucket(Path.GetFileName(source), scope == AlphabeticalOrganizeScope.Files && ignoreLeadingThe), Path.GetFileName(source));
                return new ConversionFileResult(source, File.Exists(target) || Directory.Exists(target) ? ConversionStatus.Failed : ConversionStatus.Succeeded,
                    [target], "Planned move; existing destinations block the group.");
            })).ToArray();
            var conflicts = planned.GroupBy(p => p.Outputs[0], Paths).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(Paths);
            return new(planned.Select(p => conflicts.Contains(p.Outputs[0]) ? p with { Status = ConversionStatus.Failed, Message = "Multiple inputs target the same path." } : p).ToArray(), false) { Found = planned.Length };
        }
        if (token.IsCancellationRequested) return new([], true);
        progress?.Report(new(0, 0, 0, 0, "Preparing recovery for file moves…", "00:00:00"));
        {
            var mode = WorkflowSession.Current.Value?.Data.VerificationMode ?? verificationMode;
            var plan = new List<WorkflowMoveGroup>();
            foreach (var (root, entries) in work.OrderBy(x => x.Key, Paths))
            foreach (var group in entries.Order(Paths).GroupBy(source => IsKaraokePairMember(source) ? Path.GetFileNameWithoutExtension(source) : source, Paths))
                plan.Add(new() { Items = group.Select(source => WorkflowMoves.Move(source, Path.Combine(root,
                    Bucket(Path.GetFileName(source), scope == AlphabeticalOrganizeScope.Files && ignoreLeadingThe), Path.GetFileName(source)), mode)).ToArray() });
            return WorkflowMoves.RunStandaloneOrRecipe(recovery, WorkflowSession.RootsFor(selected), scope == AlphabeticalOrganizeScope.Files ? OperationId.OrganizeFiles : OperationId.OrganizeFolders, plan, mode, saveResumableRun, progress, token);
        }

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
