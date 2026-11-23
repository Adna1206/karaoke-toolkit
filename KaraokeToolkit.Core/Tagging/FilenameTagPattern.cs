using System.Globalization;
using KaraokeToolkit.Core.Naming;

namespace KaraokeToolkit.Core.Tagging;

/// <summary>Maps fields parsed from a filename stem to karaoke media tags.</summary>
public sealed class FilenameTagPattern
{
    public const string Default = KaraokeFilenamePattern.DiscIdTrackArtistTitle;
    private readonly KaraokeFilenamePattern pattern;

    public string Pattern => pattern.Pattern;

    public FilenameTagPattern(string pattern) => this.pattern = new KaraokeFilenamePattern(pattern);

    public KaraokeTags Parse(string basename)
    {
        var parts = pattern.Parse(basename);
        uint? track = null;
        if (parts.TrackDigits is { } digits)
        {
            if (!uint.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out uint number))
                throw new FormatException("Track must be a whole number of 0 or greater.");
            track = number;
        }

        return new(parts.DiscId, track, parts.Artist, parts.Title);
    }
}

/// <summary>Null fields are absent from the template and do not replace existing values.</summary>
public sealed record KaraokeTags(string? Album, uint? Track, string? Artist, string? Title);
public sealed record TaggingOptions(bool FillEmptyOnly = false, bool ClearComments = true, bool ClearOtherDescriptiveTags = true);
/// <summary>Preview hash prevents publishing tags onto a file edited after preview.</summary>
public sealed record TaggingPreview(string Path, KaraokeTags? Tags, KaraokeTags? Existing, string? Error, string? SourceHash)
{
    public bool CanApply => Tags != null && Error == null && SourceHash != null;
}
