using KaraokeToolkit.App.Features.Settings;
using KaraokeToolkit.App.Features.Main;
using KaraokeToolkit.App.Services;
using KaraokeToolkit.Core.Settings;

namespace KaraokeToolkit.Tests.App.Settings;

public class SettingsWindowTests
{
    [Fact]
    public void SizePersistsButTabIsSessionOnlyAndUnappliedSettingsRemainUnchanged()
    {
        string path = Path.Combine(TestMediaFixture.NewFolder(), "settings.xml");
        var model = new MainViewModel(path);
        model.SaveOptions(model.Options with { AutoCreateCueText = false });
        var draft = new SettingsViewModel(model.Options) { AutoCreateCueText = true };
        var size = new SettingsWindowSize(920.5, 730.25);
        model.RememberOptionsWindow(size, 3);
        Assert.Equal(3, model.OptionsTabIndex);
        Assert.Equal(size, model.OptionsWindowSize);
        var reopened = new MainViewModel(path);
        Assert.Equal(size, reopened.OptionsWindowSize);
        Assert.Equal(0, reopened.OptionsTabIndex);
        Assert.False(reopened.Options.AutoCreateCueText);
        Assert.True(draft.HasChanges);
        reopened.SaveOptions(reopened.Options with { RecurseFolders = false });
        Assert.Equal(size, new ApplicationSettingsStore(path).LoadSettingsWindowSize());
    }

    [Theory]
    [InlineData("NaN", "640")]
    [InlineData("700", "Infinity")]
    [InlineData("0", "640")]
    [InlineData("700", "-5")]
    [InlineData("invalid", "640")]
    public void InvalidSavedDimensionsFallBackToDefaults(string width, string height)
    {
        string path = Path.Combine(TestMediaFixture.NewFolder(), "settings.xml");
        File.WriteAllText(path, $"<profile><KaraokeToolkit><SettingsWindowWidth>{width}</SettingsWindowWidth><SettingsWindowHeight>{height}</SettingsWindowHeight></KaraokeToolkit></profile>");
        Assert.Equal(new SettingsWindowSize(), new ApplicationSettingsStore(path).LoadSettingsWindowSize());
    }

    [Theory]
    [InlineData(1920, 1080, 1888, 1008)]
    [InlineData(960, 540, 928, 468)]
    [InlineData(600, 400, 568, 328)]
    public void OversizedWindowFitsWorkingAreaIncludingSmallAndScaledDisplays(double width, double height, double maxWidth, double maxHeight)
    {
        var layout = SettingsWindowLayout.Fit(new(3000, 2000), width, height);
        Assert.Equal(new SettingsWindowSize(maxWidth, maxHeight), layout.Size);
        Assert.InRange(layout.MinWidth, 1, layout.MaxWidth);
        Assert.InRange(layout.MinHeight, 1, layout.MaxHeight);
    }

    [Fact]
    public void SmallSavedWindowRespectsMinimumSizeOnLargeDisplay()
    {
        var layout = SettingsWindowLayout.Fit(new(10, 10), 1920, 1080);
        Assert.Equal(new SettingsWindowSize(660, 540), layout.Size);
        Assert.Equal(new SettingsWindowSize(), SettingsWindowLayout.Fit(new(double.NaN, 200), 1920, 1080).Size);
    }
}
