using System.Diagnostics;
using KaraokeToolkit.Core.Conversion;

namespace KaraokeToolkit.Core.Recovery;

/// <summary>A frozen move plan makes organisation/flattening restartable without re-enumerating
/// the new folder layout. A partially moved pair is completed only after both sides are verified.</summary>
internal static class WorkflowMoves
{
    internal static WorkflowMove Move(string source, string target) => new(source, target,
        Directory.Exists(source) ? WorkflowSession.TreeHash(source) : FileTransaction.Hash(source), Directory.Exists(source));

    internal static ConversionResult Run(WorkflowSession session, List<WorkflowMoveGroup>? plan,
        IProgress<ConversionProgress>? progress, CancellationToken token)
    {
        if (session.Data.Moves == null)
        { session.Data.Moves = plan ?? throw new IOException("A saved move plan is required."); session.Save(); }
        var groups = session.Data.Moves;
        var clock = Stopwatch.StartNew();
        int found = groups.Sum(g => g.Items.Length);
        foreach (var group in groups)
        {
            if (group.Results != null) continue;
            if (token.IsCancellationRequested) break;
            var results = new List<ConversionFileResult>();
            if (group.SkipReason != null)
                results.AddRange(group.Items.Select(m => new ConversionFileResult(m.Source, ConversionStatus.Skipped, [], group.SkipReason)));
            else
            {
                // Preflight the complete group before a remaining move. An existing unrelated
                // destination is a collision; a changed partially published destination blocks recovery.
                bool collision = false;
                foreach (var move in group.Items.Where(m => m.Target != null))
                {
                    RunSession.CheckPlainPath(move.Source); RunSession.CheckPlainPath(move.Target!);
                    bool source = Exists(move.Source), target = Exists(move.Target!);
                    if (source && Hash(move.Source, move.Directory) != move.Hash) throw new IOException("Move source changed: " + move.Source);
                    if (!source && (!target || Hash(move.Target!, move.Directory) != move.Hash)) throw new IOException("Interrupted move changed or disappeared: " + move.Source);
                    if (source && target) collision = true;
                }
                if (collision)
                {
                    if (group.Items.Any(m => m.Target != null && !Exists(m.Source)))
                        throw new IOException("A new destination blocks completion of a partially moved group.");
                    results.AddRange(group.Items.Select(m => new ConversionFileResult(m.Source, ConversionStatus.Skipped, [], "Destination already exists; source left in place.")));
                }
                else
                {
                    foreach (var move in group.Items)
                    {
                        using var lease = FileTransaction.AcquireLease(Path.GetDirectoryName(move.Source)!);
                        if (move.Target == null)
                        {
                            RunSession.CheckPlainPath(move.Source);
                            if (Directory.Exists(move.Source) && Directory.EnumerateFileSystemEntries(move.Source).Any())
                            { results.Add(new(move.Source, ConversionStatus.Skipped, [], "Child folder still contains items that could not be moved.")); continue; }
                            if (Directory.Exists(move.Source)) Directory.Delete(move.Source);
                            session.Data.Expected.Remove(move.Source);
                            results.Add(new(move.Source, ConversionStatus.Succeeded, [], "Removed empty child folder."));
                        }
                        else
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(move.Target)!);
                            if (Exists(move.Source))
                            {
                                if (move.Directory) Directory.Move(move.Source, move.Target); else File.Move(move.Source, move.Target);
                            }
                            if (Hash(move.Target, move.Directory) != move.Hash) throw new IOException("Moved item verification failed: " + move.Target);
                            foreach (string source in session.Data.Expected.Keys.Where(p => WorkflowSession.IsWithin(move.Source, p)).ToArray())
                            {
                                string destination = InputSelection.Paths.Equals(source, move.Source) ? move.Target : Path.Combine(move.Target, Path.GetRelativePath(move.Source, source));
                                session.Data.Expected[destination] = session.Data.Expected[source]; session.Data.Expected.Remove(source);
                            }
                            session.Data.Expected[Path.GetDirectoryName(move.Target)!] = "directory";
                            results.Add(new(move.Source, ConversionStatus.Succeeded, [move.Target], "Moved using saved recipe plan."));
                        }
                    }
                }
            }
            group.Results = results; session.Save(); // Receipt precedes the next group and progress notification.
            int completed = groups.Where(g => g.Results != null).Sum(g => g.Items.Length);
            progress?.Report(new(100, found == 0 ? 100 : completed * 100 / found, found, completed,
                $"Processed {completed} of {found} planned items.", clock.Elapsed.ToString(@"hh\:mm\:ss")));
        }
        return new(groups.Where(g => g.Results != null).SelectMany(g => g.Results!).ToArray(), token.IsCancellationRequested) { Found = found, Elapsed = clock.Elapsed };
    }
    internal static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);
    internal static string Hash(string path, bool directory) => directory ? WorkflowSession.TreeHash(path) : FileTransaction.Hash(path);
}
