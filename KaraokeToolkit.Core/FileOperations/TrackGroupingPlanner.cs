using System.Globalization;
using System.Text.RegularExpressions;
using KaraokeToolkit.Core.Archives;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Naming;
using KaraokeToolkit.Core.Recovery;

namespace KaraokeToolkit.Core.FileOperations;

public sealed record TrackGroupingOptions
{
    public bool WholeFolders { get; init; }
    public bool Recurse { get; init; }
    public bool Move { get; init; }
    public bool CreateParents { get; init; }
    public bool SkipSingleChildParents { get; init; } = true;
    public int TrailingDigits { get; init; } = 2;
    public int RangeSize { get; init; }
    public int RangeStart { get; init; } = 1;
    public int MaxParentLevels { get; init; }
    public string IdSeparator { get; init; } = "-";
    public string RangeSeparator { get; init; } = " - ";
    public string Mask { get; init; } = "x";
}

public sealed record TrackGroupingItem(string Source, string Destination, bool IsDirectory);

public sealed record TrackGroupingIssue(string Source, string Message, bool IsConflict);

public sealed record TrackGroupingPlan(IReadOnlyList<TrackGroupingItem> Items, IReadOnlyList<TrackGroupingIssue> Issues);

public enum TrackGroupingPreviewStage
{
    Grouping,
    BuildingGroups,
    CheckingDestinations,
    CheckingConflicts
}

public static partial class TrackGroupingPlanner
{
    private static readonly StringComparer Paths = InputSelection.Paths;
    private const int MaximumDigits = 9;

    [GeneratedRegex(@"^(?<prefix>[A-Za-z]+)[ _-]?(?<number>[0-9]+)(?<mask>[xX#]+)$", RegexOptions.CultureInvariant)]
    private static partial Regex FolderMask();

    [GeneratedRegex(@"^(?<prefix>[A-Za-z]+)[ _-]?(?<first>[0-9]+)\s+(?:-|to)\s+\k<prefix>[ _-]?(?<last>[0-9]+)$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex FolderRange();

    [GeneratedRegex(@"^[A-Za-z]+[ _-]?[0-9]+\s+(?:-|to)\s+[A-Za-z]+[ _-]?[0-9]+$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex IntervalLikeFolder();

    [GeneratedRegex(@"^(?:CON|PRN|AUX|NUL|COM[1-9¹²³]|LPT[1-9¹²³])(?:\.|$)",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ReservedFolderName();

    private sealed record Interval(string Prefix, long First, long Last, int Width);
    private sealed record Candidate(string Source, bool IsDirectory, Interval Group, string[] Levels);

    public static TrackGroupingPlan Plan(IReadOnlyList<string> inputs, string outputFolder, TrackGroupingOptions options,
        CancellationToken token = default)
        => Plan(inputs, outputFolder, options, token, null);

    public static TrackGroupingPlan Plan(IReadOnlyList<string> inputs, string outputFolder, TrackGroupingOptions options,
        CancellationToken token, Action<TrackGroupingPreviewStage, int, int>? report)
    {
        Validate(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputFolder);
        string output = FullPath(outputFolder);
        CheckUsablePath(output);
        if (File.Exists(output))
            throw new IOException("The output folder is an existing file: " + output);

        var selected = inputs.Select(FullPath).Distinct(Paths).Order(Paths).ToArray();
        if (selected.Length == 0)
            throw new ArgumentException("Select at least one file or folder.", nameof(inputs));

        var sources = new HashSet<string>(Paths);
        var issues = new List<TrackGroupingIssue>();
        var companionStems = new Dictionary<string, HashSet<string>>(Paths);
        foreach (string path in selected)
        {
            token.ThrowIfCancellationRequested();
            CheckUsablePath(path);
            if (Directory.Exists(path))
            {
                if (options.WholeFolders)
                    sources.Add(path);
                else
                    Scan(path);
            }
            else if (File.Exists(path))
            {
                AddFile(path);
                if (IsCompanion(path))
                {
                    string directory = Path.GetDirectoryName(path)!;
                    if (!companionStems.TryGetValue(directory, out var stems))
                        companionStems[directory] = stems = new(StringComparer.OrdinalIgnoreCase);
                    stems.Add(Path.GetFileNameWithoutExtension(path));
                }
            }
            else
                throw new FileNotFoundException("Selected input is missing: " + path, path);
        }
        foreach (var (directory, stems) in companionStems)
            foreach (string mate in Directory.EnumerateFiles(directory))
            {
                token.ThrowIfCancellationRequested();
                if (IsCompanion(mate) && stems.Contains(Path.GetFileNameWithoutExtension(mate)))
                    AddFile(mate);
            }

        var candidates = new List<Candidate>();
        Regex? customRange = CustomFolderRange(options);
        int grouped = 0;
        foreach (string source in sources.Order(Paths))
        {
            token.ThrowIfCancellationRequested();
            report?.Invoke(TrackGroupingPreviewStage.Grouping, grouped++, sources.Count);
            bool directory = Directory.Exists(source);
            if (Ancestors(source).Any(sources.Contains))
            {
                issues.Add(new(source, "The item is also inside a selected folder.", true));
                continue;
            }
            if (directory && WorkflowSession.IsWithin(source, output))
            {
                issues.Add(new(source, "The output folder is inside a selected folder.", true));
                continue;
            }
            string name = directory ? Path.GetFileName(source) : Path.GetFileNameWithoutExtension(source);
            if (Parse(name, directory, customRange) is not { } track)
            {
                issues.Add(new(source, "No track ID or supported group interval was recognized.", false));
                continue;
            }

            Interval group = Bucket(track, options);
            if (track.Last > group.Last)
            {
                issues.Add(new(source, "The source interval spans more than one destination group.", false));
                continue;
            }
            if (options.Move && OperatingSystem.IsWindows() &&
                !Paths.Equals(Path.GetPathRoot(source), Path.GetPathRoot(output)))
            {
                issues.Add(new(source, "Move requires the source and output to be on the same drive or share. Use Copy to group across drives.", true));
                continue;
            }
            if (directory)
                CheckFolderContents(source, token);
            string[] levels = Levels(group, options);
            if (levels.Any(level => level.Length > 255 || ReservedFolderName().IsMatch(level)))
            {
                issues.Add(new(source, "The naming settings generate an invalid folder name for this item.", true));
                continue;
            }
            candidates.Add(new(source, directory, group, levels));
        }

        report?.Invoke(TrackGroupingPreviewStage.BuildingGroups, 0, 0);
        var children = new Dictionary<string, HashSet<string>>(Paths);
        foreach (var candidate in candidates)
        {
            token.ThrowIfCancellationRequested();
            string parent = output;
            foreach (string level in candidate.Levels)
            {
                string current = Path.Combine(parent, level);
                if (!children.TryGetValue(parent, out var siblings))
                    children[parent] = siblings = new(Paths);
                siblings.Add(current);
                parent = current;
            }
        }

        var items = new List<TrackGroupingItem>();
        var leafIntervals = new Dictionary<string, Interval>(Paths);
        var groupFolders = new HashSet<string>(Paths);
        int checkedDestinations = 0;
        foreach (var candidate in candidates)
        {
            token.ThrowIfCancellationRequested();
            report?.Invoke(TrackGroupingPreviewStage.CheckingDestinations, checkedDestinations++, candidates.Count);
            string logicalParent = output;
            string destinationParent = output;
            for (int i = 0; i < candidate.Levels.Length; i++)
            {
                string level = candidate.Levels[i];
                logicalParent = Path.Combine(logicalParent, level);
                if (options.SkipSingleChildParents && i < candidate.Levels.Length - 1 &&
                    children[logicalParent].Count == 1)
                    continue;
                destinationParent = Path.Combine(destinationParent, level);
                groupFolders.Add(destinationParent);
            }

            if (!leafIntervals.TryAdd(destinationParent, candidate.Group) && leafIntervals[destinationParent] != candidate.Group)
                issues.Add(new(candidate.Source, "Distinct intervals generate the same group folder.", true));
            string target = Path.Combine(destinationParent, Path.GetFileName(candidate.Source));
            if (Paths.Equals(candidate.Source, target))
            {
                issues.Add(new(candidate.Source, "The item is already in its destination group.", false));
                continue;
            }
            items.Add(new(candidate.Source, target, candidate.IsDirectory));
            RunSession.CheckPlainPath(target);
            if (File.Exists(target) || Directory.Exists(target))
                issues.Add(new(candidate.Source, "Destination already exists: " + target, true));
        }

        report?.Invoke(TrackGroupingPreviewStage.CheckingConflicts, 0, 0);
        foreach (string folder in groupFolders.Order(Paths))
        {
            if (File.Exists(folder))
                issues.Add(new(folder, "A required group folder is an existing file.", true));
            if (sources.Contains(folder) || Ancestors(folder).Any(sources.Contains))
                issues.Add(new(folder, "A required group folder overlaps a selected item.", true));
        }
        foreach (var targets in items.GroupBy(i => i.Destination, Paths))
        {
            if (targets.Count() > 1)
                foreach (var item in targets)
                    issues.Add(new(item.Source, "Multiple inputs target the same path: " + targets.Key, true));
            if (groupFolders.Contains(targets.Key))
                issues.Add(new(targets.Key, "An item would occupy a required group folder.", true));
        }

        return new(items, issues);

        void AddFile(string file)
        {
            if (IsProtected(Path.GetFileName(file)))
                return;
            CheckUsablePath(file);
            sources.Add(file);
        }

        void Scan(string root)
        {
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.TryPop(out string? folder))
            {
                token.ThrowIfCancellationRequested();
                foreach (string file in Directory.EnumerateFiles(folder))
                    AddFile(file);
                if (options.Recurse)
                    foreach (string child in Directory.EnumerateDirectories(folder))
                        if (!Paths.Equals(child, output) && !IsProtected(Path.GetFileName(child)))
                        {
                            RunSession.CheckPlainPath(child);
                            pending.Push(child);
                        }
            }
        }
    }

    private static Interval? Parse(string name, bool directory, Regex? customRange)
    {
        if (directory)
        {
            var masked = FolderMask().Match(name);
            if (masked.Success)
            {
                int trailing = masked.Groups["mask"].Length;
                string digits = masked.Groups["number"].Value;
                if (digits.Length + trailing > MaximumDigits)
                    return null;
                long size = PowerOfTen(trailing);
                long first = ParseDigits(digits) * size;
                return new(masked.Groups["prefix"].Value.ToUpperInvariant(), first, first + size - 1, digits.Length + trailing);
            }

            var ranged = FolderRange().Match(name);
            if (!ranged.Success && customRange != null)
                ranged = customRange.Match(name);
            if (ranged.Success)
            {
                string start = ranged.Groups["first"].Value;
                string end = ranged.Groups["last"].Value;
                if (start.Length > MaximumDigits || end.Length > MaximumDigits || ParseDigits(start) > ParseDigits(end))
                    return null;
                return new(ranged.Groups["prefix"].Value.ToUpperInvariant(), ParseDigits(start), ParseDigits(end), start.Length);
            }
            if (IntervalLikeFolder().IsMatch(name))
                return null;
        }

        if (LeadingTrackId.Parse(name) is not { } track || track.Digits.Length > MaximumDigits)
            return null;
        long value = ParseDigits(track.Digits);
        return new(track.Prefix.ToUpperInvariant(), value, value, track.Digits.Length);
    }

    // A start-only name such as BKD-33600s could represent several interval sizes.
    // The leading ID parser requires a boundary after the number so it stays unrecognized.
    private static Regex? CustomFolderRange(TrackGroupingOptions options)
    {
        if (!options.WholeFolders || (options.IdSeparator is "-" or "" && options.RangeSeparator is " - " or " to "))
            return null;
        string id = Regex.Escape(options.IdSeparator), between = Regex.Escape(options.RangeSeparator);
        return new($@"^(?<prefix>[A-Za-z]+){id}(?<first>[0-9]+){between}\k<prefix>{id}(?<last>[0-9]+)$",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
    }

    private static Interval Bucket(Interval source, TrackGroupingOptions options)
    {
        long size = options.RangeSize > 0 ? options.RangeSize : PowerOfTen(options.TrailingDigits);
        long origin = options.RangeSize > 0 ? options.RangeStart : 0;
        long offset = source.First - origin;
        long index = offset / size;
        if (offset % size < 0)
            index--;
        long first = origin + index * size;
        return source with { First = Math.Max(first, 0), Last = first + size - 1,
            Width = options.RangeSize > 0 ? source.Width : Math.Max(source.Width, options.TrailingDigits + 1) };
    }

    private static string[] Levels(Interval group, TrackGroupingOptions options)
    {
        if (options.RangeSize > 0)
            return [Id(group, group.First, options) + options.RangeSeparator + Id(group, group.Last, options)];

        var levels = new List<string>();
        int outer = options.TrailingDigits;
        if (options.CreateParents)
        {
            outer = group.Width - 1;
            if (options.MaxParentLevels > 0)
                outer = Math.Min(outer, options.TrailingDigits + options.MaxParentLevels);
        }
        for (int digits = outer; digits >= options.TrailingDigits; digits--)
        {
            long size = PowerOfTen(digits);
            string start = Id(group, group.First / size * size, options);
            levels.Add(options.Mask == "s" ? start + "s" : start[..^digits] + new string(options.Mask[0], digits));
        }
        return levels.ToArray();
    }

    private static string Id(Interval interval, long number, TrackGroupingOptions options) =>
        interval.Prefix + options.IdSeparator + number.ToString("D" + interval.Width, CultureInfo.InvariantCulture);

    private static long PowerOfTen(int exponent)
    {
        long result = 1;
        for (int i = 0; i < exponent; i++)
            result *= 10;
        return result;
    }

    private static long ParseDigits(string digits) => long.Parse(digits, CultureInfo.InvariantCulture);

    private static void Validate(TrackGroupingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.TrailingDigits is < 1 or >= MaximumDigits)
            throw new ArgumentOutOfRangeException(nameof(options), "Trailing digits must be between 1 and 8.");
        if (options.RangeSize < 0 || options.RangeStart < 0 || options.MaxParentLevels is < 0 or >= MaximumDigits)
            throw new ArgumentOutOfRangeException(nameof(options), "Range size, starting number, and parent levels must be non-negative; parent levels may be at most 8.");
        if (options.Mask is not ("#" or "x" or "X" or "s"))
            throw new ArgumentException("Choose #, x, X, or s for the folder style.", nameof(options));
        CheckSeparator(options.IdSeparator);
        CheckSeparator(options.RangeSeparator);
    }

    private static void CheckSeparator(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Any(c => char.IsControl(c) || "<>:\"/\\|?*".Contains(c)))
            throw new ArgumentException("Separators cannot contain characters that are invalid in folder names.");
    }

    private static void CheckFolderContents(string root, CancellationToken token)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out string? folder))
        {
            token.ThrowIfCancellationRequested();
            foreach (string entry in Directory.EnumerateFileSystemEntries(folder))
            {
                CheckUsablePath(entry);
                if (Directory.Exists(entry))
                    pending.Push(entry);
            }
        }
    }

    private static bool IsCompanion(string path) => Path.GetExtension(path).Equals(".cdg", StringComparison.OrdinalIgnoreCase) ||
        KaraokeArchive.AudioExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private static bool IsProtected(string name) => InputSelection.IsExcludedFolder(name) ||
        name.Equals(".karaoke-run-lease", StringComparison.OrdinalIgnoreCase);

    private static void CheckUsablePath(string path)
    {
        RunSession.CheckPlainPath(path);
        if (path.Split(Path.DirectorySeparatorChar).Any(IsProtected))
            throw new IOException("A protected path cannot be grouped: " + path);
    }

    private static IEnumerable<string> Ancestors(string path)
    {
        for (string? parent = Path.GetDirectoryName(path); parent != null; parent = Path.GetDirectoryName(parent))
            yield return parent;
    }

    private static string FullPath(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
