using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace KaraokeToolkit.Core.Tagging;

/// <summary>A filename template: tokens select fields; all other characters are literal separators.</summary>
public sealed class FilenameTagPattern
{
    public const string Default = "%DISC%-%TRACK% - %ARTIST% - %TITLE%";
    private static readonly Regex Token = new("%([^%]+)%", RegexOptions.CultureInvariant);
    private readonly Regex matcher;
    public string Pattern { get; }
    public FilenameTagPattern(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern) || pattern.Length > 512)
            throw new ArgumentException("Enter a filename pattern of up to 512 characters.");
        var fields = new HashSet<string>(StringComparer.Ordinal);
        var expression = new StringBuilder("\\A");
        int end = 0;
        foreach (Match token in Token.Matches(pattern))
        {
            string field = token.Groups[1].Value.ToUpperInvariant();
            if (field is not ("DISC" or "TRACK" or "ARTIST" or "TITLE") || !fields.Add(field))
                throw new ArgumentException("Use DISC, TRACK, ARTIST and TITLE at most once each, enclosed in % signs.");
            if (end != 0 && token.Index == end)
                throw new ArgumentException("Put a separator between fields.");
            string separator = pattern[end..token.Index];
            if (separator.Contains('%')) throw new ArgumentException("The pattern contains an incomplete token.");
            expression.Append(Regex.Escape(separator));
            // Constrain Track during matching, so separators inside a disc identifier
            // can be backtracked over instead of being mistaken for part of the track.
            // Capture a sign too: Parse must reject negative tracks rather than silently
            // absorb the minus into the preceding disc identifier.
            expression.Append("(?<").Append(field).Append('>')
                .Append(field == "TRACK" ? "\\s*[+-]?[0-9]+\\s*" : ".+?").Append(')');
            end = token.Index + token.Length;
        }
        if (fields.Count == 0 || pattern[end..].Contains('%'))
            throw new ArgumentException("The pattern contains an incomplete or unknown token.");
        expression.Append(Regex.Escape(pattern[end..])).Append("\\z");
        matcher = new Regex(expression.ToString(), RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
        Pattern = pattern;
    }

    /// <summary>Parses a basename, never a path or extension. Track is numeric; disc remains text.</summary>
    public KaraokeTags Parse(string basename)
    {
        Match match = matcher.Match(basename);
        if (!match.Success) throw new FormatException("Filename does not match the pattern.");
        string? Read(string field)
        {
            if (!match.Groups[field].Success) return null;
            string value = match.Groups[field].Value.Trim();
            if (value.Length == 0) throw new FormatException(field + " cannot be empty.");
            return value;
        }
        string? trackText = Read("TRACK");
        uint? track = null;
        if (trackText != null)
        {
            if (!uint.TryParse(trackText, NumberStyles.None, CultureInfo.InvariantCulture, out uint number))
                throw new FormatException("Track must be a whole number of 0 or greater.");
            track = number;
        }
        return new(Read("DISC"), track, Read("ARTIST"), Read("TITLE"));
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
