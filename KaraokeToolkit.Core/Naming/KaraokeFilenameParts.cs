namespace KaraokeToolkit.Core.Naming;

/// <summary>Fields read from a filename stem. Null fields were omitted from the pattern.</summary>
public sealed record KaraokeFilenameParts(string? DiscId, string? TrackDigits, string? Artist, string? Title);
