using System.Diagnostics;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Execution;
using KaraokeToolkit.Core.FileOperations;

namespace KaraokeToolkit.Core.Recovery;

/// <summary>A frozen move plan makes organisation/flattening restartable without re-enumerating
/// the new folder layout. A partially moved pair is completed only after both sides are verified.</summary>
internal static class WorkflowMoves
{
    private static readonly EnumerationOptions RenameLookupOptions = new()
    {
        MatchCasing = MatchCasing.CaseInsensitive,
        AttributesToSkip = 0,
        IgnoreInaccessible = false
    };

    internal static WorkflowMove Move(string source, string target, RecoveryVerificationMode mode = RecoveryVerificationMode.Sha256,
        FileCheckTiming timing = FileCheckTiming.BeforeBatch)
    {
        bool directory = Directory.Exists(source);
        return new(source, target, timing == FileCheckTiming.BeforeItem ? "" : Hash(source, directory, mode), directory);
    }

    internal static FileCheckTiming TimingForStandalone(FileCheckTiming timing)
    {
        if (!Enum.IsDefined(timing)) throw new ArgumentOutOfRangeException(nameof(timing));
        return WorkflowSession.Current.Value == null ? timing : FileCheckTiming.BeforeBatch;
    }

    internal static WorkflowMoveGroup ApprovedGroup(IEnumerable<WorkflowMove> moves, RecoveryVerificationMode mode, FileCheckTiming timing)
    {
        var group = new WorkflowMoveGroup { Items = moves.ToArray() };
        if (timing == FileCheckTiming.BeforeBatch)
        {
            try { group.Items = CaptureApprovedSources(group, mode); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { group.Results = Skipped(group, error.Message); }
        }
        return group;
    }

    internal static List<ConversionFileResult> Skipped(WorkflowMoveGroup group, string reason) =>
        group.Items.Select(move => new ConversionFileResult(move.Source, ConversionStatus.Skipped, [], reason)).ToList();

    internal static void CheckApprovedGroup(WorkflowMoveGroup group, bool filenameRename, RecoveryVerificationMode mode,
        bool verifyFingerprints, bool captureFingerprints = false)
    {
        // Share ancestor checks only within this group. A later group checks current paths again.
        var paths = new MovePathChecks();
        foreach (var move in group.Items)
        {
            var source = paths.Check(move.Source, out bool sourceBlocked);
            if (sourceBlocked) source = null;
            VerifySourceKind(move, source);
            string target = move.Target!;
            bool caseOnly = filenameRename && StringComparer.OrdinalIgnoreCase.Equals(move.Source, target);
            if (caseOnly && !MovePathExists(move, move.Source, filenameRename))
                throw new IOException("The original filename has changed: " + move.Source);
            var targetAttributes = paths.Check(target, out bool blockedParent);
            if (blockedParent || (caseOnly ? MovePathExists(move, target, filenameRename) : targetAttributes != null))
                throw new IOException("Destination is occupied: " + target);
            if (verifyFingerprints && move.Hash.Length != 0 && ApprovedFingerprint(move, mode) != move.Hash)
                throw new IOException("Move source changed: " + move.Source);
        }
        if (filenameRename && HasUnexpectedRenameCompanion(group))
            throw new IOException("A destination companion already exists; source left in place.");
        if (captureFingerprints && NeedsFingerprints(group))
            group.Items = group.Items.Select(move => move with { Hash = ApprovedFingerprint(move, mode) }).ToArray();

    }

    internal sealed class MovePathChecks
    {
        private readonly Dictionary<string, FileAttributes?> ancestors = new(InputSelection.Paths);

        internal FileAttributes? Check(string path, out bool blocked)
        {
            var entry = RunSession.CheckPlainEntry(path);
            blocked = false;
            for (string? parent = Path.GetDirectoryName(path); parent != null; parent = Path.GetDirectoryName(parent))
            {
                if (!ancestors.TryGetValue(parent, out var attributes))
                    ancestors[parent] = attributes = RunSession.CheckPlainEntry(parent);
                blocked |= attributes is { } value && (value & FileAttributes.Directory) == 0;
            }
            return entry;
        }
    }

    private static WorkflowMove[] CaptureApprovedSources(WorkflowMoveGroup group, RecoveryVerificationMode mode) =>
        group.Items.Select(move =>
        {
            VerifySourceKind(move);
            return move with { Hash = ApprovedFingerprint(move, mode) };
        }).ToArray();

    private static string ApprovedFingerprint(WorkflowMove move, RecoveryVerificationMode mode) =>
        move.Directory ? WorkflowSession.TreeHash(move.Source, mode, checkEntry: CheckApprovedEntry)
            : RecoveryFingerprint.Hash(move.Source, mode);

    internal static void CheckApprovedEntry(string path)
    {
        string name = Path.GetFileName(path);
        if (InputSelection.IsExcludedFolder(name) || name.Equals(".karaoke-run-lease", StringComparison.OrdinalIgnoreCase))
            throw new IOException("The folder contains a protected item: " + path);
    }

    internal static bool IsApprovedSource(IReadOnlyList<string> selected, string source, bool includeCompanions) =>
        selected.Any(input => WorkflowSession.IsWithin(input, source) ||
            includeCompanions && FilenameRenameService.IsPairMember(input) && FilenameRenameService.IsPairMember(source) &&
            InputSelection.Paths.Equals(Path.GetDirectoryName(input), Path.GetDirectoryName(source)) &&
            StringComparer.OrdinalIgnoreCase.Equals(Path.GetFileNameWithoutExtension(input), Path.GetFileNameWithoutExtension(source)));

    internal static bool IsNormalizedPath(string path) =>
        Path.IsPathFullyQualified(path) && InputSelection.Paths.Equals(path, Path.TrimEndingDirectorySeparator(Path.GetFullPath(path))) &&
        !path.Split(Path.DirectorySeparatorChar).Any(InputSelection.IsExcludedFolder);

    internal static void CheckApprovedRecovery(RecoveryStore recovery, IEnumerable<string> folders, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var existing = folders.Distinct(InputSelection.Paths).Where(IsAvailableFolder).ToArray();
        if (recovery.FindInFolders(existing).FirstOrDefault() is { } job)
            throw new IOException("Recover interrupted work before applying the preview in: " + job.Folder);
        token.ThrowIfCancellationRequested();
    }

    internal static ConversionResult RunApproved(RecoveryStore recovery, OperationId operation, List<WorkflowMoveGroup> plan,
        RecoveryVerificationMode mode, bool saveResumableRun, IProgress<ConversionProgress>? progress, CancellationToken token,
        FileCheckTiming timing)
    {
        if (token.IsCancellationRequested) return new([], true);
        var roots = plan.SelectMany(group => group.Items).Where(move => move.Target != null)
            .Select(move => Path.GetDirectoryName(move.Source)!).Distinct(InputSelection.Paths).ToArray();
        string? workspaceRoot = roots.FirstOrDefault(IsAvailableFolder);
        if (workspaceRoot == null)
        {
            var skipped = plan.SelectMany(group => group.Results ?? Skipped(group, group.SkipReason ?? "Source folder is unavailable.")).ToArray();
            return new(skipped, false) { Found = skipped.Length };
        }
        var orderedRoots = roots.Where(root => !InputSelection.Paths.Equals(root, workspaceRoot)).Prepend(workspaceRoot).ToArray();
        return RunStandaloneOrRecipe(recovery, orderedRoots, operation, plan, mode, saveResumableRun, progress, token,
            timing, skipUnavailableMoveGroups: true);
    }

    internal static bool IsAvailableFolder(string folder)
    {
        try { return (RunSession.CheckPlainPath(folder) & FileAttributes.Directory) == FileAttributes.Directory; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
    }

    internal static ConversionResult RunStandaloneOrRecipe(RecoveryStore recovery, string[] roots, OperationId operation,
        List<WorkflowMoveGroup> plan, RecoveryVerificationMode verificationMode, bool saveResumableRun, IProgress<ConversionProgress>? progress,
        CancellationToken token, FileCheckTiming timing = FileCheckTiming.BeforeBatch, bool skipUnavailableMoveGroups = false)
    {
        if (WorkflowSession.Current.Value is { } workflow) return Run(workflow, plan, progress, token);
        if (plan.Count == 0 || token.IsCancellationRequested) return new([], token.IsCancellationRequested);
        using var session = new WorkflowSession(recovery, new WorkflowData
        {
            Kind = RecoveryKind.FileMoves, Roots = roots, OperationId = operation, Moves = plan, VerificationMode = verificationMode,
            SaveResumableRun = saveResumableRun, CheckTiming = timing, SkipUnavailableMoveGroups = skipUnavailableMoveGroups
        });
        return Complete(session, progress, token);
    }

    internal static ConversionResult Resume(RecoveryStore recovery, RecoveryJob job,
        IProgress<ConversionProgress>? progress, CancellationToken token)
    {
        using var session = new WorkflowSession(recovery, job);
        if (session.Data.Kind != RecoveryKind.FileMoves) throw new IOException("This recovery job is not a standalone file move.");
        session.VerifyExpected();
        return Complete(session, progress, token, interruptedOnly: !session.Data.SaveResumableRun);
    }

    private static ConversionResult Complete(WorkflowSession session, IProgress<ConversionProgress>? progress, CancellationToken token,
        bool interruptedOnly = false)
    {
        var result = Run(session, null, progress, token, interruptedOnly);
        if (!interruptedOnly || !result.Cancelled)
            session.Finish(interruptedMoveOnly: interruptedOnly);
        return result;
    }

    internal static ConversionResult Run(WorkflowSession session, List<WorkflowMoveGroup>? plan,
        IProgress<ConversionProgress>? progress, CancellationToken token, bool interruptedOnly = false)
    {
        if (session.Data.Moves == null)
        { session.Data.Moves = plan ?? throw new IOException("A saved move plan is required."); session.Save(); }
        var groups = session.Data.Moves;
        var clock = Stopwatch.StartNew();
        int found = groups.Sum(g => g.Items.Length);
        int completed = groups.Where(g => g.Results != null).Sum(g => g.Items.Length);
        bool trackGrouping = session.Data.Kind == RecoveryKind.TrackGrouping;
        bool filenameRename = session.Data.OperationId == OperationId.RenameByPattern;
        for (int index = 0; index < groups.Count; index++)
        {
            var group = groups[index];
            if (group.Results != null) continue;
            if (interruptedOnly && index != session.Data.ActiveMoveGroup) continue;
            if (token.IsCancellationRequested) break;
            bool freshApproval = session.Data.SkipUnavailableMoveGroups && session.Data.ActiveMoveGroup != index;
            if (session.Data.CheckTiming == FileCheckTiming.BeforeBatch && !freshApproval)
                session.BeginMoveGroup(index);
            var results = new List<ConversionFileResult>();
            using var renameLease = filenameRename && group.SkipReason == null
                ? FileTransaction.AcquireLease(Path.GetDirectoryName(group.Items[0].Source)!) : null;
            string? skipReason = group.SkipReason;
            if (freshApproval && skipReason == null)
            {
                try
                {
                    CheckApprovedGroup(group, filenameRename, session.Data.VerificationMode, verifyFingerprints: true, captureFingerprints: true);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { skipReason = error.Message; }
            }
            if (skipReason != null)
                results = Skipped(group, skipReason);
            else
            {
                // Preflight the complete group before a remaining move. An existing unrelated
                // destination is a collision; a changed partially published destination blocks recovery.
                bool collision = false;
                if (!freshApproval)
                {
                    var paths = new MovePathChecks();
                    foreach (var move in group.Items.Where(m => m.Target != null))
                    {
                        var sourceAttributes = paths.Check(move.Source, out _);
                        var targetAttributes = paths.Check(move.Target!, out bool blockedParent);
                        if (blockedParent) throw new IOException("A file blocks the move destination: " + move.Target);
                        bool caseOnly = filenameRename && StringComparer.OrdinalIgnoreCase.Equals(move.Source, move.Target);
                        bool source = caseOnly ? MovePathExists(move, move.Source, filenameRename) : sourceAttributes != null;
                        bool target = caseOnly ? MovePathExists(move, move.Target!, filenameRename) : targetAttributes != null;
                        if (source && move.Hash.Length != 0 && Hash(move.Source, move.Directory, session.Data.VerificationMode) != move.Hash)
                            throw new IOException("Move source changed: " + move.Source);
                        if (!source && (!target || Hash(move.Target!, move.Directory, session.Data.VerificationMode) != move.Hash)) throw new IOException("Interrupted move changed or disappeared: " + move.Source);
                        if (source && target) collision = true;
                    }
                    if (filenameRename && !collision)
                        collision = HasUnexpectedRenameCompanion(group);
                }
                if (collision)
                {
                    if (group.Items.Any(m => m.Target != null && !MovePathExists(m, m.Source, filenameRename)))
                        throw new IOException("A new destination blocks completion of a partially moved group.");
                    results.AddRange(group.Items.Select(m => new ConversionFileResult(m.Source, ConversionStatus.Skipped, [], "Destination already exists; source left in place.")));
                }
                else
                {
                    if (freshApproval || session.Data.CheckTiming == FileCheckTiming.BeforeItem)
                    {
                        // Save all fingerprints together before publishing any member of the group.
                        if (NeedsFingerprints(group))
                            group.Items = group.Items.Select(move => move.Target != null && move.Hash.Length == 0
                                ? move with { Hash = CaptureSource(move, session.Data.VerificationMode) }
                                : move).ToArray();
                        session.BeginMoveGroup(index);
                    }
                    foreach (var move in group.Items)
                    {
                        progress?.Report(new(0, completed * 100 / found, found, completed,
                                "Processing: " + move.Source, clock.Elapsed.ToString(@"hh\:mm\:ss"))
                                { ActivitySource = move.Source, ActivityLabel = "Processing" });
                        string sourceParent = Path.GetDirectoryName(move.Source)!;
                        bool parentAlreadyLocked = filenameRename ||
                            trackGrouping && InputSelection.Paths.Equals(sourceParent, session.Data.Roots[0]);
                        if (move.Target == null)
                        {
                            try
                            {
                                using var cleanupLease = parentAlreadyLocked ? null : FileTransaction.AcquireLease(sourceParent);
                                if (RunSession.CheckPlainPath(move.Source) is { } attributes)
                                {
                                    if ((attributes & FileAttributes.Directory) == 0)
                                        throw new IOException("The child folder was replaced by a file.");
                                    if (Directory.EnumerateFileSystemEntries(move.Source).Any())
                                    {
                                        results.Add(new(move.Source, ConversionStatus.Skipped, [], "Child folder still contains items that could not be moved."));
                                        continue;
                                    }
                                    DeleteEmptyFolder(move.Source, attributes);
                                }
                            }
                            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                            {
                                // Cleanup follows the checkpointed moves. Leave the folder in place
                                // and record the failure without stopping unrelated groups.
                                results.Add(new(move.Source, ConversionStatus.Failed, [], "Could not remove child folder: " + error.Message));
                                continue;
                            }
                            UpdateExpected(session.Data, move);
                            results.Add(new(move.Source, ConversionStatus.Succeeded, [], "Removed empty child folder."));
                        }
                        else
                        {
                            using var lease = parentAlreadyLocked ? null : FileTransaction.AcquireLease(sourceParent);
                            Directory.CreateDirectory(Path.GetDirectoryName(move.Target)!);
                            if (freshApproval || MovePathExists(move, move.Source, filenameRename))
                            {
                                try
                                {
                                    if (session.Data.Kind is RecoveryKind.TrackGrouping or RecoveryKind.FileMoves) GroupingRename.Move(move.Source, move.Target);
                                    else if (move.Directory) Directory.Move(move.Source, move.Target); else File.Move(move.Source, move.Target);
                                }
                                catch (IOException error) when ((freshApproval || filenameRename) && results.Count == 0 &&
                                    group.Items.All(item => MovePathExists(item, item.Source, filenameRename) &&
                                        Hash(item.Source, item.Directory, session.Data.VerificationMode) == item.Hash))
                                {
                                    bool occupied = freshApproval && group.Items.Any(item => MovePathExists(item, item.Target!, filenameRename));
                                    results.AddRange(occupied ? Skipped(group, error.Message) : group.Items.Select(item =>
                                        new ConversionFileResult(item.Source, ConversionStatus.Failed, [], error.Message)));
                                    break;
                                }
                            }
                            if (!MovePathExists(move, move.Target, filenameRename) ||
                                Hash(move.Target, move.Directory, session.Data.VerificationMode) != move.Hash)
                                throw new IOException("Moved item verification failed: " + move.Target);
                            UpdateExpected(session.Data, move);
                            results.Add(new(move.Source, ConversionStatus.Succeeded, [move.Target], "Moved and verified."));
                        }
                    }
                }
            }
            session.CompleteMoveGroup(index, results); // Receipt precedes the next group and progress notification.
            completed += group.Items.Length;
            progress?.Report(new(100, found == 0 ? 100 : completed * 100 / found, found, completed,
                $"Processed {completed} of {found} planned items.", clock.Elapsed.ToString(@"hh\:mm\:ss")) { ShowInActivity = false });
        }
        return new(groups.Where(g => g.Results != null).SelectMany(g => g.Results!).ToArray(), token.IsCancellationRequested) { Found = found, Elapsed = clock.Elapsed };
    }
    internal static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    internal static void UpdateExpected(WorkflowData data, WorkflowMove move)
    {
        if (move.Target == null)
        {
            data.Expected.Remove(move.Source);
            return;
        }
        if (!move.Directory)
        {
            if (data.Expected.Remove(move.Source, out string? expected)) data.Expected[move.Target] = expected;
        }
        else foreach (string source in data.Expected.Keys.Where(p => WorkflowSession.IsWithin(move.Source, p)).ToArray())
        {
            string destination = InputSelection.Paths.Equals(source, move.Source) ? move.Target : Path.Combine(move.Target, Path.GetRelativePath(move.Source, source));
            string expected = data.Expected[source];
            data.Expected.Remove(source);
            data.Expected[destination] = expected;
        }
        data.Expected[Path.GetDirectoryName(move.Target)!] = "directory";
    }

    internal static bool NeedsFingerprints(WorkflowMoveGroup group) =>
        group.Items.Any(move => move.Target != null && move.Hash.Length == 0);

    internal static void VerifySourceKind(WorkflowMove move) => VerifySourceKind(move, RunSession.CheckPlainPath(move.Source));

    private static void VerifySourceKind(WorkflowMove move, FileAttributes? attributes)
    {
        if (attributes is not { } found || ((found & FileAttributes.Directory) != 0) != move.Directory)
            throw new IOException("Source is missing or changed type: " + move.Source);
    }

    private static string CaptureSource(WorkflowMove move, RecoveryVerificationMode mode)
    {
        VerifySourceKind(move, RunSession.CheckPlainEntry(move.Source));
        return Hash(move.Source, move.Directory, mode);
    }

    internal static bool MovePathExists(WorkflowMove move, string path, bool filenameRename)
    {
        if (!filenameRename || !StringComparer.OrdinalIgnoreCase.Equals(move.Source, move.Target))
            return Exists(path);

        return Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(path)!, Path.GetFileName(path), RenameLookupOptions)
            .Contains(path, StringComparer.Ordinal);
    }

    private static bool HasUnexpectedRenameCompanion(WorkflowMoveGroup group)
    {
        string? target = group.Items.Select(move => move.Target!).FirstOrDefault(FilenameRenameService.IsPairMember);
        if (target == null) return false;
        string stem = Path.GetFileNameWithoutExtension(target);
        var expected = group.Items.Select(move => MovePathExists(move, move.Source, true) ? move.Source : move.Target!)
            .ToHashSet(StringComparer.Ordinal);
        return Directory.EnumerateFiles(Path.GetDirectoryName(target)!, stem + ".*", RenameLookupOptions).Any(path =>
            FilenameRenameService.IsPairMember(path) && !expected.Contains(path) &&
            Path.GetFileNameWithoutExtension(path).Equals(stem, StringComparison.OrdinalIgnoreCase));
    }

    private static void DeleteEmptyFolder(string path, FileAttributes attributes)
    {
        if (!OperatingSystem.IsWindows() || (attributes & FileAttributes.ReadOnly) == 0)
        {
            Directory.Delete(path);
            return;
        }

        File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
        try
        {
            Directory.Delete(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            if (RunSession.CheckPlainPath(path) is { } remaining && (remaining & FileAttributes.Directory) != 0)
                File.SetAttributes(path, remaining | FileAttributes.ReadOnly);
            throw;
        }
    }

    internal static string Hash(string path, bool directory, RecoveryVerificationMode mode) => directory ? WorkflowSession.TreeHash(path, mode) : RecoveryFingerprint.Hash(path, mode);
}
