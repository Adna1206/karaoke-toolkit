using System.Diagnostics;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Recovery;

namespace KaraokeToolkit.Core.FileOperations;

public enum FolderFlattenScope { OneLayer, AllLayers }

/// <summary>Flattens one directory layer or moves all descendant files into selected roots.</summary>
public sealed class FlattenFolderLayer
{
    private static readonly StringComparer Paths = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public Task<ConversionResult> RunAsync(IEnumerable<string> folders,
        IProgress<ConversionProgress>? progress = null, CancellationToken token = default,
        FolderFlattenScope scope = FolderFlattenScope.OneLayer) =>
        Task.Run(() => Run(folders, progress, token, scope), token);

    private static ConversionResult Run(IEnumerable<string> folders, IProgress<ConversionProgress>? progress, CancellationToken token,
        FolderFlattenScope scope)
    {
        if (!Enum.IsDefined(scope)) throw new ArgumentOutOfRangeException(nameof(scope));
        var timer = Stopwatch.StartNew();
        string[] selected = folders.Select(path => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)))
            .Distinct(Paths).ToArray();
        if (selected.Length == 0) throw new ArgumentException("Select at least one folder.");
        foreach (string folder in selected)
        {
            if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("Selected folder is missing: " + folder);
            if (Paths.Equals(folder, Path.TrimEndingDirectorySeparator(Path.GetPathRoot(folder)!)))
                throw new ArgumentException("Select a folder inside a drive or volume, not the volume root: " + folder);
            RunSession.CheckPlainPath(folder);
            if (folder.Split(Path.DirectorySeparatorChar).Any(InputSelection.IsExcludedFolder))
                throw new IOException("Recovery and Damaged folders cannot be flattened: " + folder);
        }

        // A nested selection is already covered when its parent is selected. Processing it
        // separately after a move would depend on selection order and could flatten twice.
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        string[] roots = selected.Where(folder => !selected.Any(parent => !Paths.Equals(parent, folder) &&
            folder.StartsWith(parent.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, comparison)))
            .Order(Paths).ToArray();
        var recovery = new RecoveryStore();
        foreach (string root in roots)
            if (recovery.Find(root, true).Count > 0)
                throw new IOException("Recover the interrupted conversion before flattening: " + root);

        var work = new List<(string Root, string Child, string[] Entries, bool Protected)>();
        foreach (string root in roots)
            foreach (string child in Directory.GetDirectories(root).Order(Paths))
                Plan(root, child);
        if (WorkflowSession.Current.Value is { } workflow)
        {
            var plan = new List<WorkflowMoveGroup>();
            foreach (var item in work)
                if (item.Protected)
                    plan.Add(new() { Items = item.Entries.Append(item.Child).Select(p => new WorkflowMove(p, null, "protected")).ToArray(), SkipReason = "Protected or linked child folder left in place." });
                else
                {
                    foreach (string entry in item.Entries)
                        plan.Add(new() { Items = [WorkflowMoves.Move(entry, Path.Combine(item.Root, Path.GetFileName(entry)))] });
                    plan.Add(new() { Items = [new(item.Child, null, "empty", true)] });
                }
            return WorkflowMoves.Run(workflow, plan, progress, token);
        }
        int total = work.Sum(item => item.Entries.Length + 1), completed = 0;
        var results = new List<ConversionFileResult>();
        foreach (var rootGroup in work.GroupBy(item => item.Root, Paths))
        {
            if (token.IsCancellationRequested) break;
            using var lease = FileTransaction.AcquireLease(rootGroup.Key);
            foreach (var item in rootGroup)
            {
                if (token.IsCancellationRequested) break;
                if (item.Protected || InputSelection.IsExcludedFolder(Path.GetFileName(item.Child)) || IsLinked(item.Child))
                {
                    foreach (string entry in item.Entries)
                        results.Add(new(entry, ConversionStatus.Skipped, [], "Protected or linked child folder was left in place."));
                    results.Add(new(item.Child, ConversionStatus.Skipped, [], "Protected or linked child folder was left in place."));
                    completed += item.Entries.Length + 1;
                    Report();
                    continue;
                }
                foreach (string entry in item.Entries)
                {
                    if (token.IsCancellationRequested) break;
                    string target = Path.Combine(item.Root, Path.GetFileName(entry));
                    try
                    {
                        RunSession.CheckPlainPath(entry);
                        if (File.Exists(target) || Directory.Exists(target))
                            results.Add(new(entry, ConversionStatus.Skipped, [], "An item with the same name already exists in the selected folder."));
                        else if (Directory.Exists(entry))
                        {
                            Directory.Move(entry, target);
                            results.Add(new(entry, ConversionStatus.Succeeded, [target], "Moved folder up one level."));
                        }
                        else
                        {
                            File.Move(entry, target);
                            results.Add(new(entry, ConversionStatus.Succeeded, [target], scope == FolderFlattenScope.AllLayers
                                ? "Moved file into the selected root folder." : "Moved file up one level."));
                        }
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                    {
                        results.Add(new(entry, ConversionStatus.Failed, [], error.Message));
                    }
                    completed++;
                    Report();
                }
                if (token.IsCancellationRequested) break;
                try
                {
                    if (Directory.EnumerateFileSystemEntries(item.Child).Any())
                        results.Add(new(item.Child, ConversionStatus.Skipped, [], "Child folder still contains items that could not be moved."));
                    else
                    {
                        Directory.Delete(item.Child);
                        results.Add(new(item.Child, ConversionStatus.Succeeded, [], "Removed empty child folder."));
                    }
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    results.Add(new(item.Child, ConversionStatus.Failed, [], error.Message));
                }
                completed++;
                Report();
            }
        }
        return new(results, token.IsCancellationRequested) { Found = total, Elapsed = timer.Elapsed };

        void Plan(string root, string child)
        {
            token.ThrowIfCancellationRequested();
            bool linked = IsLinked(child);
            bool isProtected = linked || InputSelection.IsExcludedFolder(Path.GetFileName(child));
            if (scope == FolderFlattenScope.AllLayers)
            {
                if (isProtected)
                {
                    work.Add((root, child, [], true));
                    return; // Never enumerate a protected subtree or follow a directory link.
                }
                RunSession.CheckPlainPath(child);
                foreach (string descendant in Directory.GetDirectories(child).Order(Paths))
                    Plan(root, descendant);
                // Children precede parents so empty directories can be removed from the bottom up.
                work.Add((root, child, Directory.GetFiles(child).Order(Paths).ToArray(), false));
            }
            else
                work.Add((root, child, linked ? [] : Directory.GetFileSystemEntries(child).Order(Paths).ToArray(), isProtected));
        }

        void Report() => progress?.Report(new(100, total == 0 ? 100 : completed * 100 / total, total, completed,
            $"Processed {completed} of {total} items and folders.", timer.Elapsed.ToString(@"hh\:mm\:ss")));
    }

    private static bool IsLinked(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
}
