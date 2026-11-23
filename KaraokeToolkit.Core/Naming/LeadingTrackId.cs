using System.Text.RegularExpressions;

namespace KaraokeToolkit.Core.Naming;

internal readonly partial record struct LeadingTrackId(string Prefix, string Separator, string Digits)
{
    internal int SeparatorIndex => Prefix.Length;

    [GeneratedRegex(@"^(?<prefix>[A-Za-z]+)(?<separator>[ _-]?)(?<digits>[0-9]+)(?=$|[\s_.-])", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    internal static LeadingTrackId? Parse(string stem)
    {
        var match = Pattern().Match(stem);
        return match.Success
            ? new(match.Groups["prefix"].Value, match.Groups["separator"].Value, match.Groups["digits"].Value)
            : null;
    }
}
