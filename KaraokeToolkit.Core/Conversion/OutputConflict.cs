namespace KaraokeToolkit.Core.Conversion;

public enum ConflictDecision
{
    Skip, Replace, Cancel
}
/// <summary>Existing destinations for one atomic publication group.</summary>
public sealed record OutputConflict(string Source, IReadOnlyList<string> ExistingFiles);
