using KaraokeToolkit.Core.Recovery;

namespace KaraokeToolkit.Core.Conversion;

/// <summary>Collects one run's verified outputs and pending listings for the desktop review.</summary>
internal sealed class ConversionConflictReview(string journal)
{
    private sealed record StagedOutput(string Path, string Hash);
    private sealed record DeferredOutput(string Destination, TransactionObservation Observation,
        StagedOutput[] Outputs, string[] Retired, string Message, OutputConflict Conflict);
    private sealed record Listing(TransactionObservation Observation, OutputConflict? Conflict);

    private readonly Dictionary<string, DeferredOutput> deferred = new(InputSelection.Paths);
    private readonly Dictionary<string, Listing> listings = new(InputSelection.Paths);
    private readonly Dictionary<OutputConflict, ConflictDecision> decisions = [];

    internal bool IsApplying { get; set; }
    internal bool HasPendingWork => deferred.Count > 0 || listings.Count > 0;
    internal IEnumerable<string> DeferredSources => deferred.Keys;
    internal IEnumerable<OutputConflict> Conflicts => deferred.Values.Select(item => item.Conflict)
        .Concat(listings.Values.Select(item => item.Conflict).OfType<OutputConflict>());

    internal void Decide(OutputConflict conflict, ConflictDecision decision) => decisions.Add(conflict, decision);

    internal void DeferIfConflicting(string source, string destination, FileTransaction transaction,
        IReadOnlyList<string> outputs, string[] retired, string message, CancellationToken token)
    {
        var observation = transaction.Observation;
        string[] existing = observation.Targets.Where(pair => pair.Value != null).Select(pair => pair.Key).ToArray();
        if (existing.Length == 0) return;

        // Disposable staging stays under the run checkpoint. An interruption before
        // publication can regenerate it from the remaining sources on recovery.
        string staging = Path.Combine(Path.GetDirectoryName(journal)!, "review-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        var files = new List<StagedOutput>();
        foreach (string output in outputs)
        {
            token.ThrowIfCancellationRequested();
            string target = Path.Combine(staging, Path.GetFileName(output));
            string hash = FileTransaction.Hash(output);
            File.Move(output, target);
            files.Add(new(target, hash));
        }
        deferred.Add(source, new(destination, observation, files.ToArray(), retired, message, new(source, existing)));
        throw new ConversionConflictDeferredException();
    }

    internal ConversionFileResult Publish(string source, CancellationToken token)
    {
        var item = deferred[source];
        if (decisions[item.Conflict] == ConflictDecision.Skip)
            throw new OutputSkippedException();

        using var transaction = new FileTransaction(item.Destination);
        transaction.RestoreObservation(item.Observation);
        var outputs = new List<string>();
        foreach (var file in item.Outputs)
        {
            token.ThrowIfCancellationRequested();
            RunSession.CheckPlainPath(file.Path);
            string staged = Path.Combine(transaction.Workspace, Path.GetFileName(file.Path));
            File.Move(file.Path, staged);
            if (FileTransaction.Hash(staged) != file.Hash)
                throw new IOException("Staged output changed while waiting for review: " + source);
            outputs.Add(staged);
        }
        return new(source, ConversionStatus.Succeeded,
            transaction.Commit(outputs, item.Retired, OverwritePolicy.Replace, token), item.Message);
    }

    internal void PlanListing(string folder, IReadOnlyList<string> targets)
    {
        var observed = new Dictionary<string, string?>(InputSelection.Paths);
        foreach (string target in targets)
            observed.Add(target, File.Exists(target) ? FileTransaction.Hash(target) : null);
        string[] existing = observed.Where(pair => pair.Value != null).Select(pair => pair.Key).ToArray();
        listings.Add(folder, new(new(new(InputSelection.Paths), observed), existing.Length == 0 ? null : new(folder, existing)));
    }

    internal OverwritePolicy ResolveListing(string folder, FileTransaction transaction)
    {
        var listing = listings[folder];
        var current = transaction.Observation;
        bool collision = listing.Conflict != null && current.Targets.Keys.Any(listing.Conflict.ExistingFiles.Contains);
        if (collision && decisions[listing.Conflict!] == ConflictDecision.Skip)
            throw new OutputSkippedException();
        if (current.Targets.Any(pair => !listing.Observation.Targets.TryGetValue(pair.Key, out var previous) || pair.Value != previous))
            throw new IOException("Listing output changed while waiting for publication: " + folder);
        return collision ? OverwritePolicy.Replace : OverwritePolicy.Fail;
    }
}

internal sealed class ConversionConflictDeferredException : Exception;
