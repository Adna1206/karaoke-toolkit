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

    public static string? GetInputError(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "Choose an existing file or folder.";
        try
        {
            string fullPath = FullPath(path);
            CheckProtectedPath(fullPath);
            return RunSession.CheckPlainPath(fullPath) == null ? "Choose an existing file or folder." : null;
        }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return error.Message;
        }
    }

    public static TrackGroupingPlan Plan(IReadOnlyList<string> inputs, string outputFolder, TrackGroupingOptions options,
        CancellationToken token = default)
        => Plan(inputs, outputFolder, options, token, null);

    public static TrackGroupingPlan Plan(IReadOnlyList<string> inputs, string outputFolder, TrackGroupingOptions options,
        CancellationToken token, Action<TrackGroupingPreviewStage, int, int>? report) =>
        Plan(inputs, outputFolder, options, token, report, null);

    public static TrackGroupingPlan PlanPreview(InputScan scan, string outputFolder, TrackGroupingOptions options,
        CancellationToken token, Action<TrackGroupingPreviewStage, int, int>? report) =>
        Plan(scan.Inputs, outputFolder, options, token, report, scan);

    private static TrackGroupingPlan Plan(IReadOnlyList<string> inputs, string outputFolder, TrackGroupingOptions options,
        CancellationToken token, Action<TrackGroupingPreviewStage, int, int>? report, InputScan? scan)
    {
        Validate(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputFolder);
        string output = FullPath(outputFolder);
        if (scan == null) CheckUsablePath(output);
        else CheckProtectedPath(output);
        bool checkDestination = scan == null || scan.CanCheckDestination(output);
        if (checkDestination && (scan?.CheckDestinationPath(output) == false || scan == null && File.Exists(output)))
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
            if (scan == null) CheckUsablePath(path);
            if (scan?.IsDirectory(path) ?? Directory.Exists(path))
            {
                if (options.WholeFolders)
                    sources.Add(path);
                else
                    Scan(path);
            }
            else if (scan != null || File.Exists(path))
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
            foreach (string mate in Files(directory))
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
            bool directory = scan?.IsDirectory(source) ?? Directory.Exists(source);
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
                CheckFolderContents(source, token, scan);
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
            if (scan == null) RunSession.CheckPlainPath(target);
            if (checkDestination && (scan == null ? File.Exists(target) || Directory.Exists(target) : scan.CheckDestinationPath(target) != null))
                issues.Add(new(candidate.Source, "Destination already exists: " + target, true));
        }

        report?.Invoke(TrackGroupingPreviewStage.CheckingConflicts, 0, 0);
        foreach (string folder in groupFolders.Order(Paths))
        {
            if (checkDestination && (scan == null ? File.Exists(folder) : scan.CheckDestinationPath(folder) == false))
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
            if (scan == null) CheckUsablePath(file);
            else scan.CheckPath(file);
            sources.Add(file);
        }

        IEnumerable<string> Files(string folder) => scan?.Files(folder) ?? Directory.EnumerateFiles(folder);

        void Scan(string root)
        {
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.TryPop(out string? folder))
            {
                token.ThrowIfCancellationRequested();
                foreach (string file in Files(folder))
                    AddFile(file);
                if (options.Recurse)
                    foreach (string child in scan?.Directories(folder) ?? Directory.EnumerateDirectories(folder))
                        if (!Paths.Equals(child, output) && !IsProtected(Path.GetFileName(child)))
                        {
                            if (scan == null) RunSession.CheckPlainPath(child);
                            else scan.CheckPath(child);
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

        if (LeadingTrackId.Parse(name, allowSuffix: true) is not { } track || track.Digits.Length > MaximumDigits)
            return null;
        // A bare start-only folder label can represent several interval sizes.
        if (directory && track.Suffix.Equals("s", StringComparison.OrdinalIgnoreCase) &&
            name.Length == track.Prefix.Length + track.Separator.Length + track.Digits.Length + track.Suffix.Length)
            return null;
        long value = ParseDigits(track.Digits);
        return new(track.Prefix.ToUpperInvariant(), value, value, track.Digits.Length);
    }

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

    private static void CheckFolderContents(string root, CancellationToken token, InputScan? scan)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out string? folder))
        {
            token.ThrowIfCancellationRequested();
            foreach (string entry in scan?.Entries(folder) ?? Directory.EnumerateFileSystemEntries(folder))
            {
                if (scan == null) CheckUsablePath(entry);
                else scan.CheckPath(entry);
                if (scan?.IsDirectory(entry) ?? Directory.Exists(entry))
                    pending.Push(entry);
            }
        }
    }

    // Retain errors with their entries so a shallow preview can ignore problems
    // in descendants that only a recursive or whole-folder plan would need.
    public sealed class InputScan
    {
        private InputScan(string[] inputs) => Inputs = inputs;

        internal IReadOnlyList<string> Inputs { get; }
        private sealed record Entry(string Path, bool IsDirectory, string? Error);
        private readonly Dictionary<string, Entry> paths = new(Paths);
        private readonly Dictionary<string, Entry[]> directories = new(Paths);
        private readonly Dictionary<string, string> readErrors = new(Paths);
        private readonly HashSet<string> walked = new(Paths);
        private string? destination;
        private string? destinationError;

        public static InputScan Read(IReadOnlyList<string> inputs, string? output, CancellationToken token)
        {
            var scan = new InputScan(inputs.Select(FullPath).Distinct(Paths).Order(Paths).ToArray());
            foreach (string input in scan.Inputs)
            {
                token.ThrowIfCancellationRequested();
                CheckUsablePath(input);
                bool isDirectory = Directory.Exists(input);
                if (!isDirectory && !File.Exists(input))
                    throw new FileNotFoundException("Selected input is missing: " + input, input);
                scan.paths[input] = new(input, isDirectory, null);
                if (isDirectory) scan.ReadTree(input, token);
                else if (IsCompanion(input)) scan.ReadDirectory(Path.GetDirectoryName(input)!, token);
            }
            if (output != null)
            {
                scan.destination = FullPath(output);
                try
                {
                    CheckUsablePath(scan.destination);
                    if (Directory.Exists(scan.destination))
                    {
                        scan.paths[scan.destination] = new(scan.destination, true, null);
                        scan.ReadTree(scan.destination, token);
                    }
                    else if (File.Exists(scan.destination))
                        scan.paths[scan.destination] = new(scan.destination, false, null);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    scan.destinationError = error.Message;
                }
            }
            return scan;
        }

        public bool CanCheckDestination(string output)
        {
            output = FullPath(output);
            return Paths.Equals(destination, output) || walked.Contains(output);
        }

        internal bool? CheckDestinationPath(string path)
        {
            foreach (string current in Ancestors(path).Prepend(path))
            {
                if (Paths.Equals(current, destination) && destinationError is { } outputError) throw new IOException(outputError);
                if (paths.TryGetValue(current, out var entry) && entry.Error is { } pathError) throw new IOException(pathError);
                if (readErrors.TryGetValue(current, out string? readError)) throw new IOException(readError);
            }
            return paths.TryGetValue(path, out var found) ? found.IsDirectory : null;
        }

        internal bool IsDirectory(string path) => paths[path].IsDirectory;
        internal void CheckPath(string path)
        {
            if (paths[path].Error is { } error) throw new IOException(error);
        }

        internal IEnumerable<string> Entries(string folder) => GetEntries(folder).Select(entry => entry.Path);
        internal IEnumerable<string> Files(string folder) => GetEntries(folder).Where(entry => !entry.IsDirectory).Select(entry => entry.Path);
        internal IEnumerable<string> Directories(string folder) => GetEntries(folder).Where(entry => entry.IsDirectory).Select(entry => entry.Path);

        private Entry[] GetEntries(string folder)
        {
            if (readErrors.TryGetValue(folder, out string? error)) throw new IOException(error);
            return directories[folder];
        }

        private void ReadTree(string root, CancellationToken token)
        {
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.TryPop(out string? folder))
            {
                token.ThrowIfCancellationRequested();
                if (!walked.Add(folder)) continue;
                ReadDirectory(folder, token);
                if (!directories.TryGetValue(folder, out var children)) continue;
                foreach (var child in children)
                    if (child.IsDirectory && child.Error == null)
                        pending.Push(child.Path);
            }
        }

        private void ReadDirectory(string folder, CancellationToken token)
        {
            if (directories.ContainsKey(folder) || readErrors.ContainsKey(folder)) return;
            try
            {
                var children = new List<Entry>();
                foreach (var item in new DirectoryInfo(folder).EnumerateFileSystemInfos())
                {
                    token.ThrowIfCancellationRequested();
                    string? error = null;
                    if ((item.Attributes & FileAttributes.ReparsePoint) != 0 || IsProtected(item.Name))
                    {
                        try { CheckUsablePath(item.FullName); }
                        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException) { error = failure.Message; }
                    }
                    var child = new Entry(item.FullName, item is DirectoryInfo, error);
                    paths[child.Path] = child;
                    children.Add(child);
                }
                directories.Add(folder, children.ToArray());
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                readErrors.Add(folder, error.Message);
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
        CheckProtectedPath(path);
    }

    private static void CheckProtectedPath(string path)
    {
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
