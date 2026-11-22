using System.Xml.Linq;
using KaraokeToolkit.Core.Archives;
using KaraokeToolkit.Core.Audio;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Settings;

namespace KaraokeToolkit.Tests.Settings;

public class SettingsTests
{
    static string FilePath() => Path.Combine(TestMediaFixture.NewFolder(), "settings.xml");

    [Fact]
    public void MissingFileUsesDocumentedDefaults()
    {
        var actual = new SettingsStore(FilePath()).Load();
        Assert.Equal(320, actual.AudioBitrate);
        Assert.Equal(AudioEncoder.MP3, actual.AudioEncoder);
        Assert.True(actual.AutoCreateCueText); Assert.True(actual.AutoRenameZip);
        Assert.True(actual.DeleteOldFiles); Assert.True(actual.RecurseFolders); Assert.True(actual.RenameBins);
        Assert.Equal(ZipCompression.Balanced, actual.ZipCompression);
        Assert.Equal("DISC-TRACK ARTIST - TITLE", actual.NamingFormat);
        Assert.Empty(actual.LastDiscNumber); Assert.Empty(actual.LastOutputFolder);
    }

    [Fact]
    public void PersistedOptionsRoundTripWithNamedEnumsAndEscapedValues()
    {
        var options = new ConversionOptions
        {
            AudioBitrate = 192, AudioEncoder = AudioEncoder.OGG, AutoCreateCueText = false,
            AutoRenameZip = false, DeleteOldFiles = false, LastDiscNumber = "DISC<&01",
            LastOutputFolder = @"C:\Music & Karaoke", NamingFormat = "ARTIST - TITLE",
            RecurseFolders = false, RenameBins = false, ZipCompression = ZipCompression.SmallestSize
        };
        string path = FilePath();
        var store = new SettingsStore(path);
        store.Save(options);
        var expected = options with { RenameBins = true };
        Assert.Equal(expected, store.Load());

        var root = XDocument.Load(path).Root!;
        Assert.Equal("1", (string?)root.Attribute("schemaVersion"));
        var entries = root.Element("Processing")!.Elements("Setting")
            .ToDictionary(e => (string)e.Attribute("id")!, e => e.Value);
        Assert.Equal("192", entries["audio.ogg-bitrate"]);
        Assert.Equal("OGG", entries["audio.encoder"]);
        Assert.Equal("false", entries["metadata.create-cue-text"]);
        Assert.Equal("false", entries["zip.auto-rename"]);
        Assert.Equal("false", entries["files.delete-originals"]);
        Assert.Equal("false", entries["files.recurse"]);
        Assert.Equal("DISC<&01", entries["disc.last-number"]);
        Assert.Equal(@"C:\Music & Karaoke", entries["files.last-output-folder"]);
        Assert.Equal("ARTIST - TITLE", entries["files.naming-format"]);
        Assert.Equal("9", entries["zip.level"]);
        Assert.False(entries.ContainsKey("files.rename-bins"));
        store.Save(store.Load());
        Assert.Equal(expected, new SettingsStore(path).Load());
    }

    [Theory]
    [InlineData("", "", "", true, true, 320)]
    [InlineData("0", "-1", "256", true, true, 256)]
    [InlineData("false", "true", "96", false, true, 96)]
    public void MissingAndInvalidSettingsFallBackIndividually(string recurse, string delete,
        string bitrate, bool expectedRecurse, bool expectedDelete, int expectedBitrate)
    {
        var doc = new XDocument(new XElement("KaraokeToolkit", new XAttribute("schemaVersion", "1"),
            new XElement("Processing",
                new XElement("Setting", new XAttribute("id", "files.recurse"), recurse),
                new XElement("Setting", new XAttribute("id", "files.delete-originals"), delete),
                new XElement("Setting", new XAttribute("id", "audio.mp3-bitrate"), bitrate))));
        string path = FilePath();
        doc.Save(path);
        Assert.Equal(new ConversionOptions
        {
            RecurseFolders = expectedRecurse, DeleteOldFiles = expectedDelete, AudioBitrate = expectedBitrate
        }, new SettingsStore(path).Load());
    }

    [Fact]
    public void SavePreservesUnknownEntriesAndSections()
    {
        string path = FilePath();
        File.WriteAllText(path, """
            <KaraokeToolkit schemaVersion="1">
              <Other><Keep>value</Keep></Other>
              <Processing><Setting id="future.setting">42</Setting></Processing>
            </KaraokeToolkit>
            """);
        new SettingsStore(path).Save(new());
        var root = XDocument.Load(path).Root!;
        Assert.Equal("value", root.Element("Other")!.Element("Keep")!.Value);
        Assert.Contains(root.Element("Processing")!.Elements("Setting"),
            field => (string?)field.Attribute("id") == "future.setting" && field.Value == "42");
    }

    [Fact]
    public void InvalidXmlIsReportedAndNotOverwritten()
    {
        string path = FilePath();
        File.WriteAllText(path, "<broken");
        var store = new SettingsStore(path);
        Assert.Throws<System.Xml.XmlException>(() => store.Load());
        Assert.Throws<System.Xml.XmlException>(() => store.Save(new()));
        Assert.Equal("<broken", File.ReadAllText(path));
    }
}
