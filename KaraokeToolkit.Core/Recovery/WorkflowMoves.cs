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

    internal static WorkflowMove Move(string source, string target, RecoveryVerificationMode mode = RecoveryVerificationMode.Sha256) => new(source, target,
        Hash(source, Directory.Exists(source), mode), Directory.Exists(source));

    internal static ConversionResult RunStandaloneOrRecipe(RecoveryStore recovery, string[] roots, OperationId operation,
        List<WorkflowMoveGroup> plan, RecoveryVerificationMode verificationMode, bool saveResumableRun, IProgress<ConversionProgress>? progress, CancellationToken token)
    {
        if (WorkflowSession.Current.Value is { } workflow) return Run(workflow, plan, progress, token);
        if (plan.Count == 0 || token.IsCancellationRequested) return new([], token.IsCancellationRequested);
        using var session = new WorkflowSession(recovery, new WorkflowData
        {
            Kind = RecoveryKind.FileMoves, Roots = roots, OperationId = operation, Moves = plan, VerificationMode = verificationMode, SaveResumableRun = saveResumableRun
        });
        return Complete(session, progress, token);
    }

    internal static ConversionResult Resume(RecoveryStore recovery, RecoveryJob job,
        IProgress<ConversionProgress>? progress, CancellationToken token)
    {
        using var session = new WorkflowSession(recovery, job);
        if (session.Data.Kind != RecoveryKind.FileMoves) throw new IOException("This recovery job is not a standalone file move.");
        session.VerifyExpected();
        return Complete(session, progress, token);
    }

    private static ConversionResult Complete(WorkflowSession session, IProgress<ConversionProgress>? progress, CancellationToken token)
    {
        var result = Run(session, null, progress, token);
        session.Finish();
        return result;
    }

    internal static ConversionResult Run(WorkflowSession session, List<WorkflowMoveGroup>? plan,
        IProgress<ConversionProgress>? progress, CancellationToken token)
    {
        if (session.Data.Moves == null)
        { session.Data.Moves = plan ?? throw new IOException("A saved move plan is required."); session.Save(); }
        var groups = session.Data.Moves;
        var clock = Stopwatch.StartNew();
        int found = groups.Sum(g => g.Items.Length);
        int completed = groups.Where(g => g.Results != null).Sum(g => g.Items.Length);
        bool trackGrouping = session.Data.Kind == RecoveryKind.TrackGrouping;
        bool filenameRename = session.Data.OperationId == OperationId.RenameByPattern;
        foreach (var group in groups)
        {
            if (group.Results != null) continue;
            if (token.IsCancellationRequested) break;
            var results = new List<ConversionFileResult>();
            if (group.SkipReason != null)
                results.AddRange(group.Items.Select(m => new ConversionFileResult(m.Source, ConversionStatus.Skipped, [], group.SkipReason)));
            else
            {
                using var renameLease = filenameRename ? FileTransaction.AcquireLease(Path.GetDirectoryName(group.Items[0].Source)!) : null;
                // Preflight the complete group before a remaining move. An existing unrelated
                // destination is a collision; a changed partially published destination blocks recovery.
                bool collision = false;
                foreach (var move in group.Items.Where(m => m.Target != null))
                {
                    RunSession.CheckPlainPath(move.Source); RunSession.CheckPlainPath(move.Target!);
                    bool source = MovePathExists(move, move.Source, filenameRename);
                    bool target = MovePathExists(move, move.Target!, filenameRename);
                    if (source && Hash(move.Source, move.Directory, session.Data.VerificationMode) != move.Hash) throw new IOException("Move source changed: " + move.Source);
                    if (!source && (!target || Hash(move.Target!, move.Directory, session.Data.VerificationMode) != move.Hash)) throw new IOException("Interrupted move changed or disappeared: " + move.Source);
                    if (source && target) collision = true;
                }
                if (filenameRename && !collision)
                    collision = HasUnexpectedRenameCompanion(group);
                if (collision)
                {
                    if (group.Items.Any(m => m.Target != null && !MovePathExists(m, m.Source, filenameRename)))
                        throw new IOException("A new destination blocks completion of a partially moved group.");
                    results.AddRange(group.Items.Select(m => new ConversionFileResult(m.Source, ConversionStatus.Skipped, [], "Destination already exists; source left in place.")));
                }
                else
                {
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
                            session.Data.Expected.Remove(move.Source);
                            results.Add(new(move.Source, ConversionStatus.Succeeded, [], "Removed empty child folder."));
                        }
                        else
                        {
                            using var lease = parentAlreadyLocked ? null : FileTransaction.AcquireLease(sourceParent);
                            Directory.CreateDirectory(Path.GetDirectoryName(move.Target)!);
                            if (MovePathExists(move, move.Source, filenameRename))
                            {
                                try
                                {
                                    if (session.Data.Kind is RecoveryKind.TrackGrouping or RecoveryKind.FileMoves) GroupingRename.Move(move.Source, move.Target);
                                    else if (move.Directory) Directory.Move(move.Source, move.Target); else File.Move(move.Source, move.Target);
                                }
                                catch (IOException error) when (filenameRename && results.Count == 0 &&
                                    group.Items.All(item => MovePathExists(item, item.Source, filenameRename) &&
                                        Hash(item.Source, false, session.Data.VerificationMode) == item.Hash))
                                {
                                    results.AddRange(group.Items.Select(item => new ConversionFileResult(item.Source,
                                        ConversionStatus.Failed, [], error.Message)));
                                    break;
                                }
                            }
                            if (!MovePathExists(move, move.Target, filenameRename) ||
                                Hash(move.Target, move.Directory, session.Data.VerificationMode) != move.Hash)
                                throw new IOException("Moved item verification failed: " + move.Target);
                            foreach (string source in session.Data.Expected.Keys.Where(p => WorkflowSession.IsWithin(move.Source, p)).ToArray())
                            {
                                string destination = InputSelection.Paths.Equals(source, move.Source) ? move.Target : Path.Combine(move.Target, Path.GetRelativePath(move.Source, source));
                                string expected = session.Data.Expected[source];
                                session.Data.Expected.Remove(source);
                                session.Data.Expected[destination] = expected;
                            }
                            session.Data.Expected[Path.GetDirectoryName(move.Target)!] = "directory";
                            results.Add(new(move.Source, ConversionStatus.Succeeded, [move.Target], "Moved and verified."));
                        }
                    }
                }
            }
            group.Results = results; session.Save(); // Receipt precedes the next group and progress notification.
            completed += group.Items.Length;
            progress?.Report(new(100, found == 0 ? 100 : completed * 100 / found, found, completed,
                $"Processed {completed} of {found} planned items.", clock.Elapsed.ToString(@"hh\:mm\:ss")) { ShowInActivity = false });
        }
        return new(groups.Where(g => g.Results != null).SelectMany(g => g.Results!).ToArray(), token.IsCancellationRequested) { Found = found, Elapsed = clock.Elapsed };
    }
    internal static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

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
