namespace KaraokeToolkit.Core.Conversion;

public enum ConflictDecision
{
    Skip, Replace, Cancel
}
/// <summary>Existing destinations for one atomic publication group.</summary>
public sealed record OutputConflict(string Source, IReadOnlyList<string> ExistingFiles);

/// <summary>Returns one choice per group in order, or null to cancel the remaining work.</summary>
public delegate Task<IReadOnlyList<ConflictDecision>?> ResolveOutputConflicts(
    IReadOnlyList<OutputConflict> conflicts, CancellationToken token);
