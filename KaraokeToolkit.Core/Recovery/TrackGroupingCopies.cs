using System.Diagnostics;
using KaraokeToolkit.Core.Conversion;

namespace KaraokeToolkit.Core.Recovery;

/// <summary>Stages an entire companion group before publishing. The prepared receipt distinguishes
/// an interrupted publication from an unrelated entry already present at a destination.</summary>
internal static class TrackGroupingCopies
{
    internal static ConversionResult Run(WorkflowSession session, IProgress<ConversionProgress>? progress, CancellationToken token)
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
            if (token.IsCancellationRequested && !group.CopyPrepared) break;
            string staging = Path.Combine(workspace, "copy-" + index);
            RunSession.CheckPlainPath(staging);
            if (!group.CopyPrepared)
            {
                if (group.Items.Any(m => WorkflowMoves.Exists(m.Target!)))
                    throw new IOException("A destination appeared after planning. Resolve the collision and resume grouping.");
                if (Directory.Exists(staging)) RecoveryWorkspaceCleanup.Delete(staging);
                Directory.CreateDirectory(staging);
                for (int i = 0; i < group.Items.Length; i++)
                {
                    var move = group.Items[i];
                    progress?.Report(new(0, completed * 100 / found, found, completed,
                        "Processing: " + move.Source, clock.Elapsed.ToString(@"hh\:mm\:ss"))
                        { ActivitySource = move.Source, ActivityLabel = "Processing" });
                    string staged = Path.Combine(staging, i.ToString());
                    Copy(move.Source, staged, move.Directory);
                    Verify(staged, move, session.Data.VerificationMode);
                }
                group.CopyPrepared = true;
                session.Save();
            }
            // Verify both states before publishing any remaining member of the group.
            for (int i = 0; i < group.Items.Length; i++)
            {
                var move = group.Items[i];
                string staged = Path.Combine(staging, i.ToString());
                RunSession.CheckPlainPath(move.Target!);
                if (WorkflowMoves.Exists(staged))
                {
                    Verify(staged, move, session.Data.VerificationMode);
                    if (WorkflowMoves.Exists(move.Target!))
                        throw new IOException("A destination blocks copy recovery: " + move.Target);
                }
                else Verify(move.Target!, move, session.Data.VerificationMode);
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
            group.Results = group.Items.Select(m => new ConversionFileResult(m.Source, ConversionStatus.Succeeded,
                [m.Target!], "Copied and verified.")).ToList();
            session.Save();
            completed += group.Items.Length;
            progress?.Report(new(100, completed * 100 / found, found, completed,
                $"Copied {completed} of {found} planned items.", clock.Elapsed.ToString(@"hh\:mm\:ss")) { ShowInActivity = false });
        }
        return new(groups.Where(g => g.Results != null).SelectMany(g => g.Results!).ToArray(), token.IsCancellationRequested)
            { Found = found, Elapsed = clock.Elapsed };
    }

    private static void Verify(string path, WorkflowMove move, RecoveryVerificationMode mode)
    {
        RunSession.CheckPlainPath(path);
        if (!WorkflowMoves.Exists(path) || WorkflowMoves.Hash(path, move.Directory, mode) != move.Hash)
            throw new IOException("Grouping copy changed or disappeared: " + path);
    }

    private static void Copy(string source, string target, bool directory)
    {
        RunSession.CheckPlainPath(source);
        if (directory)
        {
            Directory.CreateDirectory(target);
            foreach (string entry in Directory.EnumerateFileSystemEntries(source))
                Copy(entry, Path.Combine(target, Path.GetFileName(entry)), Directory.Exists(entry));
            return;
        }
        using (var input = File.OpenRead(source))
        using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            input.CopyTo(output);
            output.Flush(true);
        }
        File.SetLastWriteTimeUtc(target, File.GetLastWriteTimeUtc(source));
    }
}
