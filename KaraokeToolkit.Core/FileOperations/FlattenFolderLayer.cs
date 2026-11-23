using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Execution;
using KaraokeToolkit.Core.Recovery;

namespace KaraokeToolkit.Core.FileOperations;

public enum FolderFlattenScope { OneLayer, AllLayers }

/// <summary>Flattens one directory layer or moves all descendant files into selected roots.</summary>
public sealed class FlattenFolderLayer(RecoveryStore? recovery = null)
{
    private readonly RecoveryStore recovery = recovery ?? new();
    public Task<ConversionResult> PreviewAsync(IEnumerable<string> folders, FolderFlattenScope scope, CancellationToken token = default)
        => Task.Run(() => Run(folders, null, token, scope, true), token);
    private static readonly StringComparer Paths = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public Task<ConversionResult> RunAsync(IEnumerable<string> folders,
        IProgress<ConversionProgress>? progress = null, CancellationToken token = default,
        FolderFlattenScope scope = FolderFlattenScope.OneLayer,
        RecoveryVerificationMode verificationMode = OperationCapabilities.DefaultFileMoveRecoveryVerification, bool saveResumableRun = true,
        FileCheckTiming timing = OperationCapabilities.DefaultFileMoveCheckTiming) =>
        Task.Run(() => Run(folders, progress, token, scope, verificationMode: verificationMode, saveResumableRun: saveResumableRun, timing: timing), token);

    private ConversionResult Run(IEnumerable<string> folders, IProgress<ConversionProgress>? progress, CancellationToken token,
        FolderFlattenScope scope, bool preview = false,
        RecoveryVerificationMode verificationMode = OperationCapabilities.DefaultFileMoveRecoveryVerification, bool saveResumableRun = true,
        FileCheckTiming timing = OperationCapabilities.DefaultFileMoveCheckTiming)
    {
        if (!Enum.IsDefined(scope)) throw new ArgumentOutOfRangeException(nameof(scope));

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
        foreach (string root in roots)
            if (recovery.Find(root, true).Count > 0)
                throw new IOException("Recover the interrupted conversion before flattening: " + root);

        var work = new List<(string Root, string Child, string[] Entries, bool Protected)>();
        foreach (string root in roots)
            foreach (string child in Directory.GetDirectories(root).Order(Paths))
                Plan(root, child);
        if (preview)
        {
            var planned = work.SelectMany(w => w.Protected
                ? new[] { new ConversionFileResult(w.Child, ConversionStatus.Skipped, [], "Protected or linked child folder left in place.") }
                : w.Entries.Select(source => new ConversionFileResult(source, ConversionStatus.Succeeded,
                    [Path.Combine(w.Root, Path.GetFileName(source))], "Planned move"))
                    .Append(new(w.Child, ConversionStatus.Succeeded, [], "Remove folder only if empty after planned moves."))).ToArray();
            var duplicates = planned.Where(p => p.Outputs.Count > 0).GroupBy(p => p.Outputs[0], Paths).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(Paths);
            return new(planned.Select(p => p.Outputs.Count > 0 && (File.Exists(p.Outputs[0]) || Directory.Exists(p.Outputs[0]) || duplicates.Contains(p.Outputs[0]))
                ? p with { Status = ConversionStatus.Failed, Message = "Destination collision; move will be blocked." } : p).ToArray(), false) { Found = planned.Length };
        }
        if (token.IsCancellationRequested) return new([], true);
        progress?.Report(new(0, 0, 0, 0, "Preparing recovery for file moves…", "00:00:00") { IsIndeterminate = true });
        {
            var mode = WorkflowSession.Current.Value?.Data.VerificationMode ?? verificationMode;
            timing = WorkflowMoves.TimingForStandalone(timing);
            var plan = new List<WorkflowMoveGroup>();
            foreach (var item in work)
                if (item.Protected)
                    plan.Add(new() { Items = item.Entries.Append(item.Child).Select(p => new WorkflowMove(p, null, "protected")).ToArray(), SkipReason = "Protected or linked child folder left in place." });
                else
                {
                    foreach (string entry in item.Entries)
                        plan.Add(new() { Items = [WorkflowMoves.Move(entry, Path.Combine(item.Root, Path.GetFileName(entry)), mode, timing)] });
                    plan.Add(new() { Items = [new(item.Child, null, "empty", true)] });
                }
            return WorkflowMoves.RunStandaloneOrRecipe(recovery, roots, scope == FolderFlattenScope.OneLayer ? OperationId.FlattenOneLayer : OperationId.FlattenAllLayers,
                plan, mode, saveResumableRun, progress, token, timing);
        }

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

    }

    private static bool IsLinked(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
}
