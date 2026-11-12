using Avalonia.Controls;
using KaraokeToolkit.App.Features.Main;
using KaraokeToolkit.App.Features.Settings;

namespace KaraokeToolkit.Core.Tests.App.Settings;

public class AppearanceTests
{
    [Theory]
    [InlineData(ApplicationBackdrop.Automatic)]
    [InlineData(ApplicationBackdrop.Solid)]
    [InlineData(ApplicationBackdrop.Mica)]
    [InlineData(ApplicationBackdrop.Acrylic)]
    public void ApplyPersistsBothPreferencesAndNotifiesOnlyAfterSaving(ApplicationBackdrop backdrop)
    {
        string path = Path.Combine(TestMediaFixture.NewFolder(), "settings.xml");
        var main = new MainViewModel(path);
        var draft = new OptionsViewModel(main.Options) { Backdrop = (int)backdrop, Theme = (int)ApplicationTheme.Dark };
        ApplicationBackdrop? notified = null;
        main.BackdropChanged += value => notified = value;
        Assert.Null(notified);
        Assert.True(draft.Apply((options, policy, theme, validate, ignoreThe, bitrates, material) =>
            main.SaveOptions(options, policy, theme, validate, ignoreThe, bitrates, material)));
        Assert.Equal(backdrop, notified);
        Assert.False(draft.HasChanges);
        // Other presentation writes must preserve the applied material.
        main.RememberOptionsWindow(new(900, 700), 0);
        main.SaveOptions(main.Options);
        var reopened = new MainViewModel(path);
        Assert.Equal(backdrop, reopened.Backdrop);
        Assert.Equal(ApplicationTheme.Dark, reopened.Theme);
    }

    [Fact]
    public void ResetStagesAutomaticAndInvalidMaterialCannotBeSaved()
    {
        var model = new OptionsViewModel(new()) { Backdrop = (int)ApplicationBackdrop.Solid };
        model.MarkApplied();
        model.ResetToDefaults();
        Assert.Equal((int)ApplicationBackdrop.Automatic, model.Backdrop);
        Assert.True(model.HasChanges);
        model.Backdrop = 99;
        bool called = false;
        Assert.False(model.Apply((_, _, _, _, _, _, _) => called = true));
        Assert.False(called);
    }

    [Fact]
    public void MissingPreferenceDefaultsAndInvalidWriteIsAtomic()
    {
        string path = Path.Combine(TestMediaFixture.NewFolder(), "settings.xml");
        var store = new ApplicationSettingsStore(path);
        Assert.Equal(ApplicationBackdrop.Automatic, store.LoadBackdrop());
        store.Save(new(), theme: ApplicationTheme.Light);
        Assert.Equal(ApplicationBackdrop.Automatic, store.LoadBackdrop());
        string original = File.ReadAllText(path);
        Assert.Throws<ArgumentOutOfRangeException>(() => store.Save(new(), backdrop: (ApplicationBackdrop)99));
        Assert.Equal(original, File.ReadAllText(path));
    }

    [Theory]
    [InlineData(ApplicationBackdrop.Automatic)]
    [InlineData(ApplicationBackdrop.Solid)]
    [InlineData(ApplicationBackdrop.Mica)]
    [InlineData(ApplicationBackdrop.Acrylic)]
    public void UnsupportedPlatformsAlwaysUseSolid(ApplicationBackdrop backdrop)
    {
        Assert.Equal([WindowTransparencyLevel.None], WindowAppearance.Levels(backdrop, windows: false));
        Assert.Equal(WindowTransparencyLevel.None, WindowAppearance.Levels(backdrop, windows: true).Last());
    }

    [Fact]
    public void AutomaticPrioritizesMicaThenAcrylic()
    {
        Assert.Equal([WindowTransparencyLevel.Mica, WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.None],
            WindowAppearance.Levels(ApplicationBackdrop.Automatic, windows: true));
        Assert.Equal([WindowTransparencyLevel.Mica, WindowTransparencyLevel.None],
            WindowAppearance.Levels(ApplicationBackdrop.Mica, windows: true));
    }
}
