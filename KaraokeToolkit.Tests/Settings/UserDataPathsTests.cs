using KaraokeToolkit.Core.Settings;

namespace KaraokeToolkit.Tests.Settings;

public class UserDataPathsTests
{
    [Fact]
    public void WindowsCopiesUserDataOutOfSetupDirectoryWithoutReplacingNewSettings()
    {
        string localData = TestMediaFixture.NewFolder();
        string legacy = Path.Combine(localData, "KaraokeToolkit");
        string data = Path.Combine(localData, "KaraokeToolkitData");
        Directory.CreateDirectory(Path.Combine(legacy, "Recipes"));
        Directory.CreateDirectory(Path.Combine(legacy, "Recovery"));
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(legacy, "options.xmls"), "legacy options");
        File.WriteAllText(Path.Combine(data, "options.xmls"), "new options");
        File.WriteAllText(Path.Combine(legacy, "Recipes", "recipe.json"), "recipe");
        File.WriteAllText(Path.Combine(legacy, "Recovery", "job.json"), "index");

        string settings = UserDataPaths.DesktopSettingsPath(localData, windows: true);
        string recovery = UserDataPaths.RecoveryDirectory(localData, windows: true);

        Assert.Equal(Path.Combine(data, "options.xmls"), settings);
        Assert.Equal("new options", File.ReadAllText(settings));
        Assert.Equal("legacy options", File.ReadAllText(Path.Combine(legacy, "options.xmls")));
        Assert.Equal("recipe", File.ReadAllText(Path.Combine(data, "Recipes", "recipe.json")));
        string migratedIndex = Path.Combine(recovery, "job.json");
        Assert.Equal("index", File.ReadAllText(migratedIndex));
        File.Delete(migratedIndex);
        UserDataPaths.RecoveryDirectory(localData, windows: true);
        Assert.False(File.Exists(migratedIndex));
    }

    [Theory]
    [InlineData(true, "KaraokeToolkitData")]
    [InlineData(false, "KaraokeToolkit")]
    public void LegacyOptionsSurviveAtTheResolvedPath(bool windows, string folder)
    {
        string localData = TestMediaFixture.NewFolder();
        string legacy = Path.Combine(localData, "KaraokeToolkit");
        Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, "options.xmls"), "existing preferences");

        string path = UserDataPaths.DesktopSettingsPath(localData, windows);

        Assert.Equal(Path.Combine(localData, folder, "options.xmls"), path);
        Assert.Equal("existing preferences", File.ReadAllText(path));
    }
}
