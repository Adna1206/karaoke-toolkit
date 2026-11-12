using System.Globalization;
namespace KaraokeToolkit.Core.Archives;

/// <summary>ZIP level 0 stores files; levels 1–9 use Deflate.</summary>
public enum ZipCompression
{
    NoCompression = 0, Fast = 1, Level2 = 2, Level3 = 3, Level4 = 4,
    Balanced = 5, Level6 = 6, Level7 = 7, Level8 = 8, SmallestSize = 9
}

/// <summary>Preserves numeric levels and reads preferences saved by the former preset UI.</summary>
public static class ZipCompressionMigration
{
    public static ZipCompression FromLegacy(int level) => level is >= 0 and <= 9
        ? (ZipCompression)level
        : throw new ArgumentOutOfRangeException(nameof(level), "ZIP compression must be between 0 and 9.");

    public static int ToLegacy(ZipCompression value) => (int)FromLegacy((int)value);

    // The former Balanced preset used level 6. New preferences are written as
    // numeric strings so restoring an existing selection never changes its level.
    public static ZipCompression FromSaved(string value) => value switch
    {
        "NoCompression" => ZipCompression.NoCompression,
        "Fast" => ZipCompression.Fast,
        "Balanced" => ZipCompression.Level6,
        "SmallestSize" => ZipCompression.SmallestSize,
        _ => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int level)
            ? FromLegacy(level) : throw new InvalidDataException("Invalid ZIP compression preference.")
    };
}
