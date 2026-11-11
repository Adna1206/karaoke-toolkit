using KaraokeToolkit.App.Features.Settings;
using System.Xml;
using System.Xml.Linq;
using KaraokeToolkit.Core.Settings;

namespace KaraokeToolkit.Tests.App.Settings;

public class ApplicationSettingsStoreTests
{
    [Fact]
    public void ExistingUiPreferencesSurviveHeadlessProcessingUpdates()
    {
        string path = Path.Combine(TestMediaFixture.NewFolder(), "settings.xml");
        // Reproduce the schema written before presentation preferences moved to App.
        File.WriteAllText(path, """
            <profile><KaraokeToolkit>
              <Theme>Dark</Theme><OperationSections><FilesAndFolders>false</FilesAndFolders><FutureSection>true</FutureSection></OperationSections>
              <FavouriteOperations><Operation>ZipToBin</Operation></FavouriteOperations>
              <SettingsWindowWidth>920.5</SettingsWindowWidth><SettingsWindowHeight>730.25</SettingsWindowHeight>
              <FuturePreference>keep</FuturePreference>
            </KaraokeToolkit><section name="Options"><entry name="FutureOption">keep</entry></section></profile>
            """);
        var core = new SettingsStore(path);
        core.Save(core.Load() with { AutoCreateCueText = false });
        var app = new ApplicationSettingsStore(path);
        Assert.Equal(ApplicationTheme.Dark, app.LoadTheme());
        Assert.False(app.LoadOperationSections()["FilesAndFolders"]);
        Assert.Equal(["ZipToBin"], app.LoadFavouriteOperations());
        Assert.Equal(new SettingsWindowSize(920.5, 730.25), app.LoadSettingsWindowSize());
        Assert.False(app.Load().AutoCreateCueText);
        Assert.Equal("keep", XDocument.Load(path).Root!.Element("KaraokeToolkit")!.Element("FuturePreference")!.Value);
        app.Save(app.Load(), theme: ApplicationTheme.Light);
        var root = XDocument.Load(path).Root!;
        Assert.Null(root.Element("KaraokeToolkit")!.Element("FuturePreference"));
        Assert.DoesNotContain(root.Element("section")!.Elements("entry"), entry => (string?)entry.Attribute("name") == "FutureOption");
        Assert.False(app.LoadOperationSections().ContainsKey("FutureSection"));
        Assert.False(app.LoadOperationSections()["FilesAndFolders"]);
        Assert.Equal(["ZipToBin"], app.LoadFavouriteOperations());
        Assert.Equal(new SettingsWindowSize(920.5, 730.25), app.LoadSettingsWindowSize());
    }

    [Fact]
    public void InvalidUiUpdateLeavesProcessingAndUiPreferencesUnchanged()
    {
        string path = Path.Combine(TestMediaFixture.NewFolder(), "settings.xml");
        var app = new ApplicationSettingsStore(path);
        app.Save(new(), theme: ApplicationTheme.Dark);
        string before = File.ReadAllText(path);
        // Invalid XML name fails inside the combined write transaction.
        Assert.Throws<XmlException>(() => app.Save(new() { AutoCreateCueText = false },
            theme: ApplicationTheme.Light, operationSections: new Dictionary<string, bool> { ["invalid name"] = false }));
        Assert.Equal(before, File.ReadAllText(path));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp"));
    }

    [Fact]
    public void HeadlessCoreHasNoDesktopAssemblyDependency()
    {
        var references = typeof(SettingsStore).Assembly.GetReferencedAssemblies().Select(reference => reference.Name);
        Assert.DoesNotContain("KaraokeToolkit.App", references);
        Assert.DoesNotContain(references, name => name!.StartsWith("Avalonia", StringComparison.Ordinal));
    }
}
