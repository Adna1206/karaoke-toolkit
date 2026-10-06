using KaraokeToolkit.Core.Tagging;

namespace KaraokeToolkit.Core.Tests.Tagging;

public class FilenameTagPatternTests
{
    [Fact]
    public void DefaultMapsAlphanumericDiscToAlbumAndNumericTrack()
        => Assert.Equal(new KaraokeTags("SF123", 4, "The Beatles", "Help!"), new FilenameTagPattern(FilenameTagPattern.Default).Parse("SF123-04 - The Beatles - Help!"));

    [Theory]
    [InlineData("KJ1309-5", "04", 4u)]
    [InlineData("KJ1309-5", "0000", 0u)]
    [InlineData("KJ-1309-5", "12", 12u)]
    public void KeepsHyphenatedDiscIdentifiersTogether(string disc, string track, uint expectedTrack)
        => Assert.Equal(new KaraokeTags(disc, expectedTrack, "Lana Del Ray", "Summertime Sadness"),
            new FilenameTagPattern(FilenameTagPattern.Default).Parse($"{disc}-{track} - Lana Del Ray - Summertime Sadness"));

    [Theory]
    [InlineData("%DISC%. %ARTIST% - %TITLE%", "SF123. Artist - Title")]
    [InlineData("%TITLE% | %ARTIST% [%DISC%]", "Title | Artist [SF123]")]
    [InlineData("%artist% / %title% (%disc%)", "Artist / Title (SF123)")]
    public void UsersChooseOrderAndLiteralSeparators(string pattern, string filename)
        => Assert.Equal(new KaraokeTags("SF123", null, "Artist", "Title"), new FilenameTagPattern(pattern).Parse(filename));

    [Theory]
    [InlineData("%UNKNOWN%")]
    [InlineData("%ARTIST%-%ARTIST%")]
    [InlineData("%ARTIST%%TITLE%")]
    [InlineData("plain text")]
    [InlineData("%ARTIST% %TITLE")]
    public void RejectsInvalidTemplates(string pattern) => Assert.Throws<ArgumentException>(() => new FilenameTagPattern(pattern));

    [Theory]
    [InlineData("SF123-X - Artist - Title")]
    [InlineData("SF123--1 - Artist - Title")]
    [InlineData("SF123-999999999999 - Artist - Title")]
    [InlineData("Artist - Title")]
    public void DoesNotGuessMissingOrInvalidTracks(string filename)
        => Assert.Throws<FormatException>(() => new FilenameTagPattern(FilenameTagPattern.Default).Parse(filename));

    [Theory]
    [InlineData("0")]
    [InlineData("00")]
    [InlineData("0000")]
    public void AcceptsZeroTracks(string track)
        => Assert.Equal(0u, new FilenameTagPattern(FilenameTagPattern.Default).Parse($"DISC-{track} - Artist - Title").Track);
}
