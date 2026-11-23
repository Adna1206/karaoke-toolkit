using KaraokeToolkit.Core.Archives;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Execution;
using KaraokeToolkit.Core.Naming;
using KaraokeToolkit.Core.Recovery;

namespace KaraokeToolkit.Core.FileOperations;

public sealed record FilenameRenameRequest(IReadOnlyList<string> Inputs, string SourcePattern, string DestinationPattern,
    bool RemoveDiscWhitespace = FilenameRenameRequest.DefaultRemoveDiscWhitespace, bool Recurse = false, bool UppercaseDisc = true)
{
    public const bool DefaultRemoveDiscWhitespace = true;
}

public sealed record FilenameRenameItem(string Source, string? Destination, string Group, string? Issue = null, bool IsConflict = false);

public sealed record FilenameRenamePlan(IReadOnlyList<FilenameRenameItem> Items)
{
    public bool CanApply => Items.Any(item => item.Destination != null) && !Items.Any(item => item.IsConflict);
}

/// <summary>Plans same-folder filename changes and publishes the approved names through move recovery.</summary>
public sealed class FilenameRenameService(RecoveryStore? recovery = null)
{
    private readonly RecoveryStore recovery = recovery ?? new();
    private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;
    public static IReadOnlyList<string> Extensions { get; } = Array.AsReadOnly(InputSelection.Extensions.ToArray());

    public Task<FilenameRenamePlan> PreviewAsync(FilenameRenameRequest request, CancellationToken token = default) =>
        Task.Run(() => Plan(request, token), token);

    public Task<ConversionResult> ApplyAsync(FilenameRenameRequest request, FilenameRenamePlan approved,
        RecoveryVerificationMode verification, bool saveResumableRun,
        IProgress<ConversionProgress>? progress = null, CancellationToken token = default,
        FileCheckTiming timing = OperationCapabilities.DefaultFileMoveCheckTiming) =>
        Task.Run(() => Apply(request, approved, verification, saveResumableRun, progress, token, timing), token);

    public sealed class PreviewSnapshot
    {
        internal PreviewSnapshot(IReadOnlyList<FolderScan> shallow, IReadOnlyList<FolderScan>? recursive, string? recursiveError)
        {
            Shallow = shallow;
            Recursive = recursive;
            RecursiveError = recursiveError;
        }

        internal IReadOnlyList<FolderScan> Shallow { get; }
        internal IReadOnlyList<FolderScan>? Recursive { get; }
        internal string? RecursiveError { get; }
    }
    internal sealed record FolderScan(string Path, Dictionary<string, int> Existing, HashSet<string> PairedStems, RenameGroup[] Groups,
        string[] Children);
    internal sealed record RenameGroup(string[] Sources, string? Error);

    public static PreviewSnapshot ReadPreview(IReadOnlyList<string> inputs, CancellationToken token)
    {
        var listings = new Dictionary<string, FolderScan>(InputSelection.Paths);
        var shallow = Scan(inputs, false, ReadFolder, token);
        try
        {
            return new(shallow, Scan(inputs, true, ReadFolder, token), null);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return new(shallow, null, error.Message);
        }

        FolderScan ReadFolder(string folder)
        {
            if (!listings.TryGetValue(folder, out var entries))
                listings[folder] = entries = ReadDirectory(folder, token);
            return entries;
        }
    }

    public static FilenameRenamePlan PlanPreview(FilenameRenameRequest request, PreviewSnapshot snapshot, CancellationToken token) =>
        Plan(request, token, request.Recurse ? snapshot.Recursive ?? throw new IOException(snapshot.RecursiveError) : snapshot.Shallow);

    private static IReadOnlyList<FolderScan> Scan(IReadOnlyList<string> inputs, bool recurse,
        Func<string, FolderScan> readFolder, CancellationToken token)
    {
        var folders = new Dictionary<string, HashSet<string>?>(InputSelection.Paths);
        foreach (string input in InputSelection.Normalize(inputs))
        {
            token.ThrowIfCancellationRequested();
            if (Directory.Exists(input))
            {
                var pending = new Stack<string>();
                pending.Push(input);
                while (pending.TryPop(out string? folder))
                {
                    token.ThrowIfCancellationRequested();
                    folders[folder] = null;
                    if (recurse)
                        foreach (string child in readFolder(folder).Children)
                            pending.Push(child);
                }
            }
            else
            {
                if (!IsSupported(input)) throw new ArgumentException("Unsupported rename input: " + input);
                string parent = Path.GetDirectoryName(input)!;
                if (!folders.TryGetValue(parent, out var selected))
                    folders[parent] = selected = new(InputSelection.Paths);
                selected?.Add(input);
            }
        }

        var result = new List<FolderScan>();
        foreach (var (folder, selected) in folders.OrderBy(pair => pair.Key, InputSelection.Paths))
        {
            token.ThrowIfCancellationRequested();
            var entry = readFolder(folder);
            result.Add(selected == null ? entry : entry with { Groups = entry.Groups.Where(group => group.Sources.Any(selected.Contains)).ToArray() });
        }
        return result;
    }

    private static FolderScan ReadDirectory(string folder, CancellationToken token)
    {
        var entries = new DirectoryInfo(folder).GetFileSystemInfos();
        var existing = entries.CountBy(entry => entry.FullName, Names).ToDictionary(Names);
        var media = entries.OfType<FileInfo>().Select(entry => entry.FullName).Where(IsSupported).Order(InputSelection.Paths);
        var groups = media.GroupBy(GroupKey, Names).ToArray();
        var pairedStems = groups.Where(group => IsPairMember(group.First())).Select(group => group.Key).ToHashSet(Names);
        var result = new List<RenameGroup>();
        foreach (var group in groups)
        {
            token.ThrowIfCancellationRequested();
            var sources = group.ToArray();
            string? error = null;
            try
            {
                foreach (string source in sources) RunSession.CheckPlainPath(source);
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException) { error = failure.Message; }
            result.Add(new(sources, error));
        }
        var children = entries.OfType<DirectoryInfo>().Where(entry =>
            !InputSelection.IsExcludedFolder(entry.Name) && (entry.Attributes & FileAttributes.ReparsePoint) == 0)
            .Select(entry => entry.FullName).ToArray();
        return new(folder, existing, pairedStems, result.ToArray(), children);
    }

    private static FilenameRenamePlan Plan(FilenameRenameRequest request, CancellationToken token, IReadOnlyList<FolderScan>? snapshot = null)
    {
        var sourcePattern = new KaraokeFilenamePattern(request.SourcePattern);
        var destinationPattern = new KaraokeFilenamePattern(request.DestinationPattern);
        var folders = snapshot ?? Scan(request.Inputs, request.Recurse, folder => ReadDirectory(folder, token), token);
        var items = new List<FilenameRenameItem>();
        foreach (var folder in folders)
        foreach (var group in folder.Groups)
        {
            token.ThrowIfCancellationRequested();
            items.AddRange(PlanGroup(folder, group, sourcePattern, destinationPattern, request, checkLiveDestination: snapshot == null));
        }
        var duplicateTargets = items.Where(item => item.Destination != null)
            .GroupBy(item => GroupKey(item.Destination!), Names)
            .Where(group => group.Select(item => item.Group).Distinct(InputSelection.Paths).Skip(1).Any() ||
                group.Select(item => item.Destination).Distinct(Names).Count() != group.Count())
            .Select(group => group.Key).ToHashSet(Names);
        return new(items.Select(item => item.Destination != null && duplicateTargets.Contains(GroupKey(item.Destination))
            ? item with { Issue = "Multiple inputs would use the same destination name.", IsConflict = true }
            : item).ToArray());
    }

    private static FilenameRenameItem[] PlanGroup(FolderScan folder, RenameGroup group,
        KaraokeFilenamePattern sourcePattern, KaraokeFilenamePattern destinationPattern,
        FilenameRenameRequest request, bool checkLiveDestination)
    {
        var sources = group.Sources;
        string first = sources[0];
        if (group.Error is { } issue)
            return sources.Select(path => new FilenameRenameItem(path, null, first, issue)).ToArray();
        try
        {
            var parts = sourcePattern.Parse(Path.GetFileNameWithoutExtension(first));
            if (parts.DiscId is { } disc)
            {
                if (request.RemoveDiscWhitespace)
                    disc = string.Concat(disc.Where(character => !char.IsWhiteSpace(character)));
                if (request.UppercaseDisc)
                    disc = disc.ToUpperInvariant();
                parts = parts with { DiscId = disc };
            }
            string stem = destinationPattern.Format(parts);
            return sources.Select(path =>
            {
                string target = Path.Combine(folder.Path, MetadataFileNames.ValidateAndAppendExtension(stem, Path.GetExtension(path)));
                if (StringComparer.Ordinal.Equals(path, target))
                    return new FilenameRenameItem(path, null, first, "Name already matches.");
                bool occupied = Names.Equals(path, target)
                    ? folder.Existing.GetValueOrDefault(target) > 1
                    : folder.Existing.ContainsKey(target) || (checkLiveDestination && WorkflowMoves.Exists(target));
                bool collision = occupied ||
                    IsPairMember(path) && folder.PairedStems.Contains(GroupKey(target)) && !Names.Equals(GroupKey(path), GroupKey(target));
                return new FilenameRenameItem(path, target, first,
                    collision ? "The destination name is already in use." : null, collision);
            }).ToArray();
        }
        catch (Exception error) when (error is FormatException or ArgumentException or IOException or UnauthorizedAccessException
                                      or System.Text.RegularExpressions.RegexMatchTimeoutException)
        {
            return sources.Select(path => new FilenameRenameItem(path, null, first, error.Message)).ToArray();
        }
    }

    private ConversionResult Apply(FilenameRenameRequest request, FilenameRenamePlan approved,
        RecoveryVerificationMode verification, bool saveResumableRun, IProgress<ConversionProgress>? progress,
        CancellationToken token, FileCheckTiming timing)
    {
        if (!Enum.IsDefined(verification)) throw new ArgumentException("Unknown recovery verification mode.");
        timing = WorkflowMoves.TimingForStandalone(timing);
        if (!approved.CanApply) throw new InvalidOperationException("There are no applicable renames, or a destination conflict blocks the batch.");

        var selected = request.Inputs.Select(path => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path))).ToArray();
        var ready = approved.Items.Where(item => item.Destination != null).ToArray();
        var sources = new HashSet<string>(InputSelection.Paths);
        var targets = new HashSet<string>(Names);
        if (ready.Any(item => !WorkflowMoves.IsNormalizedPath(item.Source) || !WorkflowMoves.IsNormalizedPath(item.Destination!) ||
            !WorkflowMoves.IsApprovedSource(selected, item.Source, includeCompanions: true) ||
            !InputSelection.Paths.Equals(Path.GetDirectoryName(item.Source), Path.GetDirectoryName(item.Destination)) ||
            !InputSelection.Paths.Equals(Path.GetDirectoryName(item.Source), Path.GetDirectoryName(item.Group)) ||
            StringComparer.Ordinal.Equals(item.Source, item.Destination) ||
            !Names.Equals(Path.GetExtension(item.Source), Path.GetExtension(item.Destination)) ||
            !sources.Add(item.Source) || !targets.Add(item.Destination!)))
            throw new ArgumentException("The approved rename plan contains an invalid source or destination.");
        WorkflowMoves.CheckApprovedRecovery(recovery, ready.Select(item => Path.GetDirectoryName(item.Source)!), token);

        progress?.Report(new(0, 0, approved.Items.Count, 0, "Preparing recovery for renames…", "00:00:00") { IsIndeterminate = true });
        var issues = approved.Items.Where(item => item.Destination == null)
            .Select(item => new ConversionFileResult(item.Source, ConversionStatus.Skipped, [], item.Issue ?? "No change."));
        var moves = ready.GroupBy(item => item.Group, InputSelection.Paths).Select(group =>
        {
            token.ThrowIfCancellationRequested();
            return WorkflowMoves.ApprovedGroup(group.Select(item => new WorkflowMove(item.Source, item.Destination, "")), verification, timing);
        }).ToList();

        var result = WorkflowMoves.RunApproved(recovery, OperationId.RenameByPattern, moves,
            verification, saveResumableRun, progress, token, timing);
        return result with { Files = result.Files.Concat(issues).ToArray(), Found = approved.Items.Count };
    }

    private static bool IsSupported(string path) => Extensions.Contains(Path.GetExtension(path), Names);

    internal static bool IsPairMember(string path) =>
        Names.Equals(Path.GetExtension(path), ".cdg") || KaraokeArchive.AudioExtensions.Contains(Path.GetExtension(path), Names);

    private static string GroupKey(string path) => IsPairMember(path) ? "pair:" + Path.ChangeExtension(path, null) : "file:" + path;
}
