using KaraokeToolkit.Core.Conversion;

namespace KaraokeToolkit.Core.Recovery;

/// <summary>Durable progress records for a standalone move or grouping copy plan.</summary>
internal sealed class MoveCheckpointLog : IDisposable
{
    internal const int WorkflowVersion = 3;
    private const string Filename = "moves.jsonl";
    private readonly CheckpointLog<Record> log;

    private MoveCheckpointLog(CheckpointLog<Record> log) => this.log = log;

    internal static MoveCheckpointLog Create(string journal) => new(CheckpointLog<Record>.Create(LogPath(journal)));

    internal static MoveCheckpointLog Open(string journal, int committedLength) =>
        new(CheckpointLog<Record>.Open(LogPath(journal), committedLength));

    internal void Begin(int index, WorkflowMoveGroup group) =>
        log.Append(new(index, group.Items.Select(move => move.Hash).ToArray(), null));

    internal void PrepareCopy(int index, WorkflowMove[] items) =>
        log.Append(new(index, items.Select(move => move.Hash).ToArray(), null, CopyPrepared: true));

    internal void Complete(int index, List<ConversionFileResult> results) => log.Append(new(index, null, results));

    internal static int Replay(string journal, WorkflowData data)
    {
        var groups = data.Moves ?? throw new IOException("The move checkpoint has no plan.");
        if (data.ActiveMoveGroup != null || groups.Any(group => group.CopyPrepared))
            throw new IOException("Unexpected active or prepared group in the initial plan.");
        int next = 0;
        while (next < groups.Count && groups[next].Results != null) next++;
        return CheckpointLog<Record>.Replay(LogPath(journal), record =>
        {
            if (record.Index != next || next >= groups.Count ||
                (record.Hashes == null) == (record.Results == null) || record.CopyPrepared && record.Hashes == null) throw InvalidRecord();
            var group = groups[next];
            if (record.Hashes is { } hashes)
            {
                if (hashes.Length != group.Items.Length || (record.CopyPrepared
                    ? !data.CopyGrouping || data.ActiveMoveGroup != next || group.CopyPrepared
                    : data.ActiveMoveGroup != null)) throw InvalidRecord();
                bool startingCopy = data.CopyGrouping && !record.CopyPrepared;
                for (int i = 0; i < hashes.Length; i++)
                {
                    var move = group.Items[i];
                    string hash = hashes[i];
                    if (hash == null || ((startingCopy || move.Hash.Length != 0) && move.Hash != hash) ||
                        (!startingCopy && move.Target != null && !RecoveryFingerprint.IsValid(hash, data.VerificationMode))) throw InvalidRecord();
                    group.Items[i] = move with { Hash = hash };
                }
                data.ActiveMoveGroup = next;
                group.CopyPrepared = record.CopyPrepared;
            }
            else
            {
                var results = record.Results!;
                if (results.Count != group.Items.Length) throw InvalidRecord();
                for (int i = 0; i < results.Count; i++)
                {
                    var move = group.Items[i];
                    var result = results[i];
                    bool succeeded = result?.Status == ConversionStatus.Succeeded;
                    if (result == null || !InputSelection.Paths.Equals(move.Source, result.Source) ||
                        result.Status is not (ConversionStatus.Succeeded or ConversionStatus.Skipped or ConversionStatus.Failed) ||
                        succeeded && (data.ActiveMoveGroup != next || data.CopyGrouping && !group.CopyPrepared) || result.Outputs == null ||
                        (succeeded && move.Target != null
                            ? result.Outputs.Count != 1 || !InputSelection.Paths.Equals(move.Target, result.Outputs[0])
                            : result.Outputs.Count != 0)) throw InvalidRecord();
                    if (succeeded && !data.CopyGrouping) WorkflowMoves.UpdateExpected(data, move);
                }
                group.Results = results;
                data.ActiveMoveGroup = null;
                do { next++; } while (next < groups.Count && groups[next].Results != null);
            }
        });
    }

    private static string LogPath(string journal) => Path.Combine(Path.GetDirectoryName(journal)!, Filename);
    private static IOException InvalidRecord() => new("Invalid file group progress record. Keep the journal for manual review.");
    private sealed record Record(int Index, string[]? Hashes, List<ConversionFileResult>? Results, bool CopyPrepared = false);

    public void Dispose() => log.Dispose();
}
