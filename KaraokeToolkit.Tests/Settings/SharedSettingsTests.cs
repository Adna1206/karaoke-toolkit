using System.Xml.Linq;
using KaraokeToolkit.Core.Recovery;
using KaraokeToolkit.Core.Settings;

namespace KaraokeToolkit.Tests.Settings;

public class SharedSettingsTests
{
    [Fact]
    public void SharedWritesUseOneVersionedProcessingSection()
    {
        string path = Path.Combine(TestMediaFixture.NewFolder(), "settings.xml");
        var store = new SharedSettingsStore(path);
        store.Set("files.recurse", "false", SettingsClient.Cli);
        store.Set("zip.level", "8", SettingsClient.Mcp);

        var root = XDocument.Load(path).Root!;
        Assert.Equal("KaraokeToolkit", root.Name);
        Assert.Equal("1", (string?)root.Attribute("schemaVersion"));
        var fields = root.Element("Processing")!.Elements("Setting").ToArray();
        Assert.Equal(["files.recurse", "zip.level"], fields.Select(field => (string?)field.Attribute("id")));
        Assert.Equal(["false", "8"], fields.Select(field => field.Value));
        var reader = new SettingsStore(path);
        Assert.Equal(8, (int)reader.Load().ZipCompression);
        Assert.False(reader.Load().RecurseFolders);
    }

    [Fact]
    public void ReleaseValuesApplyInMemoryAndAreCommittedExactlyOnce()
    {
        string path = Path.Combine(TestMediaFixture.NewFolder(), "settings.xml");
        const string original = """
            <KaraokeToolkit schemaVersion="1">
              <Processing>
                <Setting id="recovery.verification">Sha256</Setting>
                <Setting id="recovery.batch-resume">false</Setting>
              </Processing>
              <Desktop><Theme>Dark</Theme></Desktop>
            </KaraokeToolkit>
            """;
        File.WriteAllText(path, original);
        var store = new SharedSettingsStore(path);

        Assert.Equal(RecoveryVerificationMode.XxHash128, store.Load().RecoveryVerification);
        Assert.Equal(original, File.ReadAllText(path));
        store.ApplyMigrations();
        Assert.Equal(RecoveryVerificationMode.XxHash128, new SettingsStore(path).LoadRecoveryPreferences().Mode);
        store.Set("recovery.verification", "Sha256", SettingsClient.Cli);
        string updated = File.ReadAllText(path);

        store.ApplyMigrations();
        Assert.Equal(updated, File.ReadAllText(path));
        Assert.Equal(RecoveryVerificationMode.Sha256, store.Load().RecoveryVerification);
        Assert.False(store.Load().SaveResumableRun);
        var root = XDocument.Load(path).Root!;
        Assert.Equal("Dark", root.Element("Desktop")!.Element("Theme")!.Value);
        Assert.Equal(2, root.Element("AppliedSettingsMigrations")!.Elements("Id").Count());
    }

    [Fact]
    public void CutoverUsesDefaultsAndDeletesOnlyKnownFilesOnce()
    {
        string localData = TestMediaFixture.NewFolder();
        string path = UserDataPaths.DesktopSettingsPath(localData, windows: true);
        var oldFiles = UserDataPaths.RetiredOptionsFiles(localData, windows: true);
        foreach (string oldFile in oldFiles)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(oldFile)!);
            File.WriteAllText(oldFile, "<malformed");
            File.WriteAllText(oldFile + ".bak", "keep");
        }
        var store = WithCleanup(path, oldFiles);

        Assert.Equal(RecoveryVerificationMode.XxHash128, store.Load().RecoveryVerification);
        Assert.False(File.Exists(path));
        Assert.All(oldFiles, oldFile => Assert.True(File.Exists(oldFile)));
        store.ApplyMigrations();
        Assert.Equal(new KaraokeToolkit.Core.Conversion.ConversionOptions(), store.Load().Options);
        Assert.All(oldFiles, oldFile =>
        {
            Assert.False(File.Exists(oldFile));
            Assert.True(File.Exists(oldFile + ".bak"));
        });
        Assert.Contains("2026-11-remove-options-xmls", XDocument.Load(path).Root!
            .Element("AppliedSettingsMigrations")!.Elements("Id").Select(id => id.Value));

        File.WriteAllText(oldFiles[0], "written later by an old build");
        store.ApplyMigrations();
        Assert.True(File.Exists(oldFiles[0]));
    }

    [Fact]
    public void FailedCleanupRetriesWithoutChangingSavedSettings()
    {
        string folder = TestMediaFixture.NewFolder();
        string path = Path.Combine(folder, "settings.xml");
        string oldFile = Path.Combine(folder, "options.xmls");
        Directory.CreateDirectory(oldFile);
        var store = WithCleanup(path, [oldFile]);
        store.Set("files.recurse", "false", SettingsClient.Mcp);
        Assert.DoesNotContain("2026-11-remove-options-xmls", File.ReadAllText(path));
        Assert.True(Directory.Exists(oldFile));

        Directory.Delete(oldFile);
        File.WriteAllText(oldFile, "old settings");
        store.ApplyMigrations();

        Assert.False(File.Exists(oldFile));
        Assert.Contains("2026-11-remove-options-xmls", File.ReadAllText(path));
        Assert.False(store.Load().Options.RecurseFolders);
    }

    [Theory]
    [InlineData("<broken")]
    [InlineData("<KaraokeToolkit schemaVersion='99' />")]
    [InlineData("<KaraokeToolkit schemaVersion='1'><Processing><Setting id='files.recurse'>true</Setting><Setting id='files.recurse'>false</Setting></Processing></KaraokeToolkit>")]
    public void InvalidProfilePreventsCleanup(string contents)
    {
        string folder = TestMediaFixture.NewFolder();
        string path = Path.Combine(folder, "settings.xml");
        string oldFile = Path.Combine(folder, "options.xmls");
        File.WriteAllText(path, contents);
        File.WriteAllText(oldFile, "keep");
        var store = WithCleanup(path, [oldFile]);

        Assert.NotNull(Record.Exception(store.ApplyMigrations));
        Assert.Equal(contents, File.ReadAllText(path));
        Assert.Equal("keep", File.ReadAllText(oldFile));
    }

    [Fact]
    public void FailedProfileWriteLeavesOldFileInPlace()
    {
        string folder = TestMediaFixture.NewFolder();
        string path = Path.Combine(folder, "settings.xml");
        string oldFile = Path.Combine(folder, "options.xmls");
        const string profile = "<KaraokeToolkit schemaVersion='1' />";
        File.WriteAllText(path, profile);
        File.WriteAllText(oldFile, "keep");
        var store = WithCleanup(path, [oldFile]);

        using (var held = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.Throws<IOException>(store.ApplyMigrations);

        Assert.Equal(profile, File.ReadAllText(path));
        Assert.Equal("keep", File.ReadAllText(oldFile));
    }

    [Fact]
    public void NewProfileAndInvalidWritesKeepTheIntendedValue()
    {
        string path = Path.Combine(TestMediaFixture.NewFolder(), "settings.xml");
        var store = new SharedSettingsStore(path);
        Assert.Equal(RecoveryVerificationMode.XxHash128, store.Load().RecoveryVerification);
        Assert.False(File.Exists(path));
        store.Set("recovery.verification", "MetadataOnly", SettingsClient.Mcp);
        string saved = File.ReadAllText(path);
        Assert.Throws<ArgumentException>(() => store.Set("recovery.verification", "1", SettingsClient.Mcp));
        Assert.Throws<ArgumentException>(() => store.Set("files.delete-originals", "true", SettingsClient.Cli));
        Assert.Equal(saved, File.ReadAllText(path));
        Assert.Equal(RecoveryVerificationMode.MetadataOnly, store.Load().RecoveryVerification);
    }

    [Fact]
    public void CatalogueExposesTypedDefaultsAndExplicitAccessDecisions()
    {
        var store = new SharedSettingsStore(Path.Combine(TestMediaFixture.NewFolder(), "settings.xml"));
        foreach (var setting in SharedSettingCatalog.All.Values)
        {
            Assert.True(Enum.IsDefined(setting.Cli));
            Assert.True(Enum.IsDefined(setting.Mcp));
        }
        var exposed = store.List(SettingsClient.Mcp);
        Assert.DoesNotContain(exposed, setting => setting.Id == "files.delete-originals");
        var verification = Assert.Single(exposed, setting => setting.Id == "recovery.verification");
        Assert.Equal("enum", verification.Type);
        Assert.Equal(RecoveryVerificationMode.XxHash128, verification.Default);
        Assert.Contains("Sha256", verification.Choices);
    }

    private static SharedSettingsStore WithCleanup(string path, IReadOnlyList<string> oldFiles) =>
        new(new SettingsStore(path, persistRenameBins: true, applyReleaseMigrations: true) { RetiredOptionsFiles = oldFiles });
}
