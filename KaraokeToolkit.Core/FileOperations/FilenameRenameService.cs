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
        IProgress<ConversionProgress>? progress = null, CancellationToken token = default) =>
        Task.Run(() => Apply(request, approved, verification, saveResumableRun, progress, token), token);

    private static FilenameRenamePlan Plan(FilenameRenameRequest request, CancellationToken token)
    {
        var sourcePattern = new KaraokeFilenamePattern(request.SourcePattern);
        var destinationPattern = new KaraokeFilenamePattern(request.DestinationPattern);
        var folders = new Dictionary<string, HashSet<string>?>(InputSelection.Paths);
        foreach (string input in InputSelection.Normalize(request.Inputs))
        {
            token.ThrowIfCancellationRequested();
            if (Directory.Exists(input))
            {
                foreach (string folder in ConversionService.EnumerateFolders(input, request.Recurse))
                {
                    token.ThrowIfCancellationRequested();
                    folders[folder] = null;
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

        var items = new List<FilenameRenameItem>();
        foreach (var (folder, selected) in folders.OrderBy(pair => pair.Key, InputSelection.Paths))
        {
            token.ThrowIfCancellationRequested();
            var entries = Directory.GetFileSystemEntries(folder);
            var existing = entries.CountBy(path => path, Names).ToDictionary(Names);
            var media = entries.Where(p => IsSupported(p) && File.Exists(p)).Order(InputSelection.Paths);
            var groups = media.GroupBy(GroupKey, Names).ToArray();
            var pairedStems = groups.Where(group => IsPairMember(group.First())).Select(group => group.Key).ToHashSet(Names);
            foreach (var group in groups)
            {
                token.ThrowIfCancellationRequested();
                if (selected != null && !group.Any(selected.Contains)) continue;
                items.AddRange(PlanGroup(folder, group, existing, pairedStems, sourcePattern, destinationPattern, request));
            }
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

    private static FilenameRenameItem[] PlanGroup(string folder, IEnumerable<string> group, Dictionary<string, int> existing,
        HashSet<string> pairedStems, KaraokeFilenamePattern sourcePattern, KaraokeFilenamePattern destinationPattern,
        FilenameRenameRequest request)
    {
        var sources = group.ToArray();
        string first = sources[0];
        try
        {
            foreach (string path in sources) RunSession.CheckPlainPath(path);
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
                string target = Path.Combine(folder, MetadataFileNames.ValidateAndAppendExtension(stem, Path.GetExtension(path)));
                if (StringComparer.Ordinal.Equals(path, target))
                    return new FilenameRenameItem(path, null, first, "Name already matches.");
                bool occupied = Names.Equals(path, target)
                    ? existing.GetValueOrDefault(target) > 1
                    : existing.ContainsKey(target) || WorkflowMoves.Exists(target);
                bool collision = occupied ||
                    IsPairMember(path) && pairedStems.Contains(GroupKey(target)) && !Names.Equals(GroupKey(path), GroupKey(target));
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
        CancellationToken token)
    {
        if (!Enum.IsDefined(verification)) throw new ArgumentException("Unknown recovery verification mode.");
        var plan = Plan(request, token);
        if (!approved.Items.SequenceEqual(plan.Items))
            throw new IOException("The rename plan changed. Refresh the preview before applying it.");
        if (!plan.CanApply) throw new InvalidOperationException("There are no applicable renames, or a destination conflict blocks the batch.");

        string[] roots = WorkflowSession.RootsFor(InputSelection.Normalize(request.Inputs));
        foreach (string root in roots)
            if (recovery.Find(root, request.Recurse).Count > 0)
                throw new IOException("Recover interrupted work before renaming items in: " + root);

        progress?.Report(new(0, 0, plan.Items.Count, 0, "Preparing recovery for renames…", "00:00:00") { IsIndeterminate = true });
        var issues = plan.Items.Where(item => item.Destination == null)
            .Select(item => new ConversionFileResult(item.Source, ConversionStatus.Skipped, [], item.Issue ?? "No change.")).ToList();
        var moves = new List<WorkflowMoveGroup>();
        foreach (var group in plan.Items.Where(item => item.Destination != null).GroupBy(item => item.Group, InputSelection.Paths))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                moves.Add(new() { Items = group.Select(item => WorkflowMoves.Move(item.Source, item.Destination!, verification)).ToArray() });
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                issues.AddRange(group.Select(item => new ConversionFileResult(item.Source, ConversionStatus.Failed, [], error.Message)));
            }
        }

        var result = WorkflowMoves.RunStandaloneOrRecipe(recovery, roots, OperationId.RenameByPattern, moves,
            verification, saveResumableRun, progress, token);
        return result with { Files = result.Files.Concat(issues).ToArray(), Found = plan.Items.Count };
    }

    private static bool IsSupported(string path) => Extensions.Contains(Path.GetExtension(path), Names);

    internal static bool IsPairMember(string path) =>
        Names.Equals(Path.GetExtension(path), ".cdg") || KaraokeArchive.AudioExtensions.Contains(Path.GetExtension(path), Names);

    private static string GroupKey(string path) => IsPairMember(path) ? "pair:" + Path.ChangeExtension(path, null) : "file:" + path;
}
