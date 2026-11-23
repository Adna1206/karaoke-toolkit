using System.Text.RegularExpressions;

namespace KaraokeToolkit.Core.Naming;

internal readonly partial record struct LeadingTrackId(string Prefix, string Separator, string Digits, string Suffix)
{
    internal int SeparatorIndex => Prefix.Length;

    [GeneratedRegex(@"^(?<prefix>[A-Za-z]+)(?<separator>[ _-]?)(?<digits>[0-9]+)(?<suffix>[A-Za-z]*)(?=$|[\s_.-])", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    internal static LeadingTrackId? Parse(string stem, bool allowSuffix = false)
    {
        var match = Pattern().Match(stem);
        var suffix = match.Groups["suffix"];
        return match.Success && (allowSuffix || suffix.Length == 0)
            ? new(match.Groups["prefix"].Value, match.Groups["separator"].Value, match.Groups["digits"].Value, suffix.Value)
            : null;
    }
}
