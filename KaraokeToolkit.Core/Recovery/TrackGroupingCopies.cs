using System.Diagnostics;
using KaraokeToolkit.Core.Conversion;

namespace KaraokeToolkit.Core.Recovery;

/// <summary>Stages an entire companion group before publishing. The prepared receipt distinguishes
/// an interrupted publication from an unrelated entry already present at a destination.</summary>
internal static class TrackGroupingCopies
{
    internal static ConversionResult Run(WorkflowSession session, IProgress<ConversionProgress>? progress, CancellationToken token,
        bool interruptedOnly = false)
    {
        var clock = Stopwatch.StartNew();
        var groups = session.Data.Moves!;
        int found = groups.Sum(g => g.Items.Length);
        int completed = groups.Where(g => g.Results != null).Sum(g => g.Items.Length);
        string workspace = Path.GetDirectoryName(session.Journal)!;

        for (int index = 0; index < groups.Count; index++)
        {
            var group = groups[index];
            if (group.Results != null) continue;
            if (interruptedOnly && index != session.Data.ActiveMoveGroup) continue;
            if (token.IsCancellationRequested && !group.CopyPrepared) break;
            session.BeginMoveGroup(index);
            string staging = Path.Combine(workspace, "copy-" + index);
            RunSession.CheckPlainPath(staging);
            if (!group.CopyPrepared)
            {
                if (Directory.Exists(staging)) RecoveryWorkspaceCleanup.DeleteGroupingCopy(workspace, index);
                bool capture = WorkflowMoves.NeedsFingerprints(group);
                var verified = capture ? (WorkflowMove[])group.Items.Clone() : group.Items;
                try
                {
                    if (session.Data.SkipUnavailableMoveGroups)
                        WorkflowMoves.CheckApprovedGroup(group, filenameRename: false, session.Data.VerificationMode, verifyFingerprints: false);
                    else if (group.Items.Any(m => WorkflowMoves.Exists(m.Target!)))
                        throw new IOException("A destination appeared after planning. Resolve the collision and resume grouping.");
                    Directory.CreateDirectory(staging);
                    for (int i = 0; i < group.Items.Length; i++)
                    {
                        var move = group.Items[i];
                        progress?.Report(new(0, completed * 100 / found, found, completed,
                            "Processing: " + move.Source, clock.Elapsed.ToString(@"hh\:mm\:ss"))
                            { ActivitySource = move.Source, ActivityLabel = "Processing" });
                        string staged = Path.Combine(staging, i.ToString());
                        WorkflowMoves.VerifySourceKind(move);
                        string fingerprint = CopyAndFingerprint(move, staged, capture ? session.Data.VerificationMode : RecoveryVerificationMode.None,
                            session.Data.SkipUnavailableMoveGroups);
                        if (capture)
                        {
                            move = move with { Hash = fingerprint };
                            verified[i] = move;
                        }
                        Verify(staged, move, session.Data.VerificationMode);
                    }
                    if (session.Data.SkipUnavailableMoveGroups)
                        WorkflowMoves.CheckApprovedGroup(group, filenameRename: false, session.Data.VerificationMode, verifyFingerprints: false);
                }
                catch (Exception error) when (session.Data.SkipUnavailableMoveGroups && (error is IOException or UnauthorizedAccessException))
                {
                    // No copy can reach a destination before CopyPrepared is persisted.
                    if (Directory.Exists(staging)) RecoveryWorkspaceCleanup.DeleteGroupingCopy(workspace, index);
                    CompleteGroup(WorkflowMoves.Skipped(group, error.Message));
                    continue;
                }
                session.PrepareCopyGroup(index, verified);
            }
            // Verify both states before publishing any remaining member of the group.
            var paths = new WorkflowMoves.MovePathChecks();
            for (int i = 0; i < group.Items.Length; i++)
            {
                var move = group.Items[i];
                string staged = Path.Combine(staging, i.ToString());
                var target = paths.Check(move.Target!, out bool blockedTarget);
                if (blockedTarget) throw new IOException("A file blocks the grouping destination: " + move.Target);
                var stagedAttributes = paths.Check(staged, out bool blockedStaging);
                if (blockedStaging) throw new IOException("A file blocks the grouping workspace: " + staged);
                if (stagedAttributes != null)
                {
                    Verify(staged, move, session.Data.VerificationMode, stagedAttributes);
                    if (target != null)
                        throw new IOException("A destination blocks copy recovery: " + move.Target);
                }
                else Verify(move.Target!, move, session.Data.VerificationMode, target);
            }
            for (int i = 0; i < group.Items.Length; i++)
            {
                var move = group.Items[i];
                string staged = Path.Combine(staging, i.ToString());
                if (WorkflowMoves.Exists(staged))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(move.Target!)!);
                    GroupingRename.Move(staged, move.Target!);
                }
                Verify(move.Target!, move, session.Data.VerificationMode);
            }
            CompleteGroup(group.Items.Select(m => new ConversionFileResult(m.Source, ConversionStatus.Succeeded,
                [m.Target!], "Copied and verified.")).ToList());

            void CompleteGroup(List<ConversionFileResult> results)
            {
                session.CompleteMoveGroup(index, results);
                completed += group.Items.Length;
                progress?.Report(new(100, completed * 100 / found, found, completed,
                    $"Processed {completed} of {found} planned items.", clock.Elapsed.ToString(@"hh\:mm\:ss")) { ShowInActivity = false });
            }
        }
        return new(groups.Where(g => g.Results != null).SelectMany(g => g.Results!).ToArray(), token.IsCancellationRequested)
            { Found = found, Elapsed = clock.Elapsed };
    }

    private static void Verify(string path, WorkflowMove move, RecoveryVerificationMode mode) =>
        Verify(path, move, mode, RunSession.CheckPlainPath(path));

    private static void Verify(string path, WorkflowMove move, RecoveryVerificationMode mode, FileAttributes? attributes)
    {
        if (attributes is not { } found || ((found & FileAttributes.Directory) != 0) != move.Directory ||
            WorkflowMoves.Hash(path, move.Directory, mode) != move.Hash)
            throw new IOException("Grouping copy changed or disappeared: " + path);
    }

    private static string CopyAndFingerprint(WorkflowMove move, string target, RecoveryVerificationMode mode, bool approved)
    {
        if (!move.Directory) return FileStaging.CopyForGrouping(move.Source, target, mode);
        return WorkflowSession.TreeHash(move.Source, mode,
            visitDirectory: path => Directory.CreateDirectory(Destination(path)),
            fingerprintFile: path => FileStaging.CopyForGrouping(path, Destination(path), mode),
            checkEntry: approved ? WorkflowMoves.CheckApprovedEntry : null);

        string Destination(string path) => Path.Combine(target, Path.GetRelativePath(move.Source, path));
    }
}
