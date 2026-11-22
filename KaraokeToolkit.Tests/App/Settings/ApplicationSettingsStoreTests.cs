using KaraokeToolkit.App.Features.Settings;
using System.Xml;
using System.Xml.Linq;
using KaraokeToolkit.Core.Settings;
using KaraokeToolkit.Core.Recovery;

namespace KaraokeToolkit.Tests.App.Settings;

public class ApplicationSettingsStoreTests
{
    [Fact]
    public void RecoveryVerificationDefaultsToXxHash128AndHonorsSavedSha256()
    {
        string path = Path.Combine(TestMediaFixture.NewFolder(), "settings.xml");
        var app = new ApplicationSettingsStore(path);
        app.Save(new());
        Assert.Equal((RecoveryVerificationMode.XxHash128, true), app.LoadRecoveryPreferences());

        app.Save(new(), recoveryVerification: RecoveryVerificationMode.Sha256, saveResumableRun: false);
        Assert.Equal((RecoveryVerificationMode.Sha256, false), new ApplicationSettingsStore(path).LoadRecoveryPreferences());
    }

    [Fact]
    public void ExistingUiPreferencesSurviveHeadlessProcessingUpdates()
    {
        string path = Path.Combine(TestMediaFixture.NewFolder(), "settings.xml");
        // Include unknown entries written by a later compatible version.
        File.WriteAllText(path, """
            <KaraokeToolkit schemaVersion="1"><Desktop>
              <Theme>Dark</Theme><OperationSections><FilesAndFolders>false</FilesAndFolders><FutureSection>true</FutureSection></OperationSections>
              <FavouriteOperations><Operation>ZipToBin</Operation></FavouriteOperations>
              <SettingsWindowWidth>920.5</SettingsWindowWidth><SettingsWindowHeight>730.25</SettingsWindowHeight>
              <FuturePreference>keep</FuturePreference>
            </Desktop><Processing><Setting id="future.option">keep</Setting></Processing></KaraokeToolkit>
            """);
        var core = new SettingsStore(path);
        core.Save(core.Load() with { AutoCreateCueText = false });
        var app = new ApplicationSettingsStore(path);
        Assert.Equal(ApplicationTheme.Dark, app.LoadTheme());
        Assert.False(app.LoadOperationSections()["FilesAndFolders"]);
        Assert.Equal(["ZipToBin"], app.LoadFavouriteOperations());
        Assert.Equal(new SettingsWindowSize(920.5, 730.25), app.LoadSettingsWindowSize());
        Assert.False(app.Load().AutoCreateCueText);
        Assert.Equal("keep", XDocument.Load(path).Root!.Element("Desktop")!.Element("FuturePreference")!.Value);
        app.Save(app.Load(), theme: ApplicationTheme.Light);
        var root = XDocument.Load(path).Root!;
        Assert.Equal("keep", root.Element("Desktop")!.Element("FuturePreference")!.Value);
        Assert.Contains(root.Element("Processing")!.Elements("Setting"), entry => (string?)entry.Attribute("id") == "future.option");
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
