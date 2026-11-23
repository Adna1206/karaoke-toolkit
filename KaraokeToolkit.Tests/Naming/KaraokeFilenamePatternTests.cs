using KaraokeToolkit.Core.Naming;

namespace KaraokeToolkit.Tests.Naming;

public class KaraokeFilenamePatternTests
{
    [Theory]
    [InlineData(KaraokeFilenamePattern.DiscIdArtistTitle, "ABC123-5-04", null)]
    [InlineData(KaraokeFilenamePattern.DiscIdTrackArtistTitle, "ABC123-5", "04")]
    public void PresetDeterminesWhetherTrackDigitsArePartOfTheDiscId(string preset, string discId, string? trackDigits)
    {
        var pattern = new KaraokeFilenamePattern(preset);

        Assert.Equal(new KaraokeFilenameParts(discId, trackDigits, "Artist", "Title"),
            pattern.Parse("ABC123-5-04 - Artist - Title"));
    }

    [Theory]
    [InlineData("%DISC%-%TRACK% - %TITLE% - %ARTIST%", "ABC123-04 - Open Road - The Lanterns", "ABC123", "04")]
    [InlineData("%ARTIST% - %TITLE% - %DISC%-%TRACK%", "The Lanterns - Open Road - ABC123-04", "ABC123", "04")]
    [InlineData("%TITLE% - %ARTIST% - %DISC%-%TRACK%", "Open Road - The Lanterns - ABC123-04", "ABC123", "04")]
    [InlineData("%ARTIST% - %TITLE%", "The Lanterns - Open Road", null, null)]
    [InlineData("%TITLE% - %ARTIST%", "Open Road - The Lanterns", null, null)]
    public void PresetReadsFieldsInFilenameOrder(string preset, string stem, string? discId, string? trackDigits)
    {
        Assert.Contains(preset, KaraokeFilenamePattern.Presets);
        Assert.Equal(new KaraokeFilenameParts(discId, trackDigits, "The Lanterns", "Open Road"),
            new KaraokeFilenamePattern(preset).Parse(stem));
    }
}
