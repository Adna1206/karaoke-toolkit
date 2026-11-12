using KaraokeToolkit.Core.Audio;
using KaraokeToolkit.Core.Archives;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Settings;

using System.Xml.Linq;
using KaraokeToolkit.Core;
namespace KaraokeToolkit.Core.Tests.Settings;

public class SettingsTests
{
    static string FilePath() => Path.Combine(TestMediaFixture.NewFolder(), "settings.xmls");
    [Fact]
    public void MissingFileUsesDocumentedDefaults()
    {
        var actual = new SettingsStore(FilePath()).Load();
        Assert.Equal(128, actual.AudioBitrate);
        Assert.Equal(AudioEncoder.MP3, actual.AudioEncoder);
        Assert.True(actual.AutoCreateCueText); Assert.True(actual.AutoRenameZip);
        Assert.True(actual.DeleteOldFiles); Assert.True(actual.RecurseFolders); Assert.True(actual.RenameBins);
        Assert.Equal(ZipCompression.Balanced, actual.ZipCompression);
        Assert.Equal("DISC-TRACK ARTIST - TITLE", actual.NamingFormat);
        Assert.Empty(actual.LastDiscNumber); Assert.Empty(actual.LastOutputFolder);
    }

    [Fact]
    public void PersistedOptionsRoundTripAndRetainXmlSchemaAndEscapedValues()
    {
        var options = new ConversionOptions
        {
            AudioBitrate = 192, AudioEncoder = AudioEncoder.OGG, AutoCreateCueText = false,
            AutoRenameZip = false, DeleteOldFiles = false, LastDiscNumber = "DISC<&01",
            LastOutputFolder = "C:\\Music & Karaoke", NamingFormat = "ARTIST - TITLE",
            RecurseFolders = false, RenameBins = false, ZipCompression = ZipCompression.SmallestSize
        };
        string path = FilePath(); var store = new SettingsStore(path); store.Save(options);
        var expected = options with { RenameBins = true }; // Compatibility-mode schema omits this preference.
        Assert.Equal(expected, store.Load());
        var doc = XDocument.Load(path);
        var entries = doc.Root!.Elements("section").Single(e => (string?)e.Attribute("name") == "Options")
            .Elements("entry").ToDictionary(e => (string)e.Attribute("name")!, e => e.Value);
        Assert.Equal("192", entries["AudioBitrate"]);
        Assert.Equal("0", entries["AudioEncoder"]);
        Assert.Equal("False", entries["AutoCreateCueText"]);
        Assert.Equal("False", entries["AutoRenameZip"]);
        Assert.Equal("False", entries["DeleteOldFiles"]);
        Assert.Equal("False", entries["RecurseFolders"]);
        Assert.Equal("DISC<&01", entries["LastDiscNumber"]);
        Assert.Equal("C:\\Music & Karaoke", entries["LastOutputFolder"]);
        Assert.Equal("ARTIST - TITLE", entries["NamingFormat"]);
        Assert.Equal("9", entries["ZipCompressionLevel"]);
        Assert.False(entries.ContainsKey("RenameBins"));
        store.Save(store.Load());
        Assert.Equal(expected, new SettingsStore(path).Load());
    }

    [Fact]
    public void HandwrittenPreviousSchemaLoadsAndResavesWithoutChangingValues()
    {
        string path = FilePath();
        File.WriteAllText(path, """
            <profile><section name="Options">
              <entry name="AudioBitrate">192</entry><entry name="AudioEncoder">0</entry>
              <entry name="RecurseFolders">False</entry><entry name="DeleteOldFiles">False</entry>
              <entry name="AutoCreateCueText">False</entry><entry name="AutoRenameZip">False</entry>
              <entry name="LastDiscNumber">DISC&lt;&amp;01</entry>
              <entry name="LastOutputFolder">C:\Music &amp; Karaoke</entry>
              <entry name="NamingFormat">ARTIST - TITLE</entry><entry name="ZipCompressionLevel">9</entry>
            </section></profile>
            """);
        var expected = new ConversionOptions
        {
            AudioBitrate = 192, AudioEncoder = AudioEncoder.OGG, RecurseFolders = false, DeleteOldFiles = false,
            AutoCreateCueText = false, AutoRenameZip = false, LastDiscNumber = "DISC<&01",
            LastOutputFolder = @"C:\Music & Karaoke", NamingFormat = "ARTIST - TITLE", ZipCompression = ZipCompression.SmallestSize
        };
        var store = new SettingsStore(path);
        Assert.Equal(expected, store.Load()); store.Save(store.Load());
        Assert.Equal(expected, store.Load());
    }

    [Theory]
    [InlineData("", "", "", true, true, 128)]
    [InlineData("0", "-1", "256", false, true, 256)]
    [InlineData("False", "True", "96", false, true, 96)]
    public void PartialEmptyAndVbBooleanValuesHaveExplicitExpectedValues(string recurse, string delete,
        string bitrate, bool expectedRecurse, bool expectedDelete, int expectedBitrate)
    {
        var doc = new XDocument(new XElement("profile", new XElement("section", new XAttribute("name", "Options"),
            new XElement("entry", new XAttribute("name", "RecurseFolders"), recurse),
            new XElement("entry", new XAttribute("name", "DeleteOldFiles"), delete),
            new XElement("entry", new XAttribute("name", "AudioBitrate"), bitrate))));
        string path = FilePath(); File.WriteAllText(path, doc.ToString());
        Assert.Equal(new ConversionOptions
        {
            RecurseFolders = expectedRecurse, DeleteOldFiles = expectedDelete, AudioBitrate = expectedBitrate
        }, new SettingsStore(path).Load());
    }

    [Fact]
    public void SavePreservesUnknownEntriesAndSections()
    {
        string path=FilePath();File.WriteAllText(path,"<profile><section name='Other'><entry name='keep'>value</entry></section><section name='Options'><entry name='Future'>42</entry></section></profile>");
        new SettingsStore(path).Save(new());var doc=XDocument.Load(path);
        Assert.Contains(doc.Descendants("entry"),e=>(string?)e.Attribute("name")=="keep"&&e.Value=="value");
        Assert.Contains(doc.Descendants("entry"),e=>(string?)e.Attribute("name")=="Future"&&e.Value=="42");
        Assert.DoesNotContain(doc.Descendants("entry"),e=>(string?)e.Attribute("name")=="RenameBins");
    }
    [Fact]
    public void InvalidXmlIsReportedAndNotOverwritten()
    {
        string path=FilePath();File.WriteAllText(path,"<broken");var store=new SettingsStore(path);
        Assert.Throws<System.Xml.XmlException>(()=>store.Load());Assert.Throws<System.Xml.XmlException>(()=>store.Save(new()));
        Assert.Equal("<broken",File.ReadAllText(path));
    }
}

