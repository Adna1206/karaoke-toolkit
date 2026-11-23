using KaraokeToolkit.Core.Video;
using KaraokeToolkit.Core.Audio;
using KaraokeToolkit.Core.Archives;
using KaraokeToolkit.App.Features.Settings;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Settings;
using KaraokeToolkit.Core.Recovery;
using KaraokeToolkit.Core;
using Xunit;

namespace KaraokeToolkit.Tests.App.Settings;

public class SettingsApplyTests
{
    [Fact]
    public void ResetDefaultsStagesAllEditablePreferencesUntilApplied()
    {
        var original = new ConversionOptions
        {
            ExperimentalDiscRipping = true, CdrdaoExecutablePath = "custom cdrdao",
            AudioEncoder = AudioEncoder.OGG, AudioBitrate = 320, AutoCreateCueText = false,
            DeleteOldFiles = false, RecurseFolders = false, AutoRenameZip = false,
            RenameBins = false, NamingFormat = "custom", ZipCompression = ZipCompression.SmallestSize,
            ZipAlgorithm = ZipAlgorithm.Lzma, VideoContainer = VideoContainer.AVI,
            VideoResolution = VideoResolution.FullHD, VideoFrameRate = 60, VideoBitrateKbps = 6000,
            VideoAudioMatchSourceBitrate = false, VideoAudioBitrateKbps = 320,
            LastOutputFolder = "remembered folder", LastDiscNumber = "remembered disc"
        };
        var model = new SettingsViewModel(original, new AudioBitratePreferences(320, 320, 320, 320))
        {
            Theme = (int)ApplicationTheme.Dark, Overwrite = (int)OverwritePolicy.Replace,
            ValidateBeforeConversion = true, IgnoreLeadingTheForFileOrganization = true,
            RecoveryVerification = (int)RecoveryVerificationMode.Sha256, CheckTiming = (int)FileCheckTiming.BeforeBatch
        };
        model.MarkApplied();
        model.ResetToDefaults();
        Assert.True(model.HasChanges);
        Assert.Equal(new ConversionOptions() with { LastOutputFolder = original.LastOutputFolder, LastDiscNumber = original.LastDiscNumber }, model.ToOptions());
        Assert.Equal((int)ApplicationTheme.System, model.Theme);
        Assert.Equal((int)OverwritePolicy.Ask, model.Overwrite);
        Assert.Equal((int)RecoveryVerificationMode.XxHash128, model.RecoveryVerification);
        Assert.Equal((int)FileCheckTiming.BeforeItem, model.CheckTiming);
        Assert.False(model.ValidateBeforeConversion);
        Assert.False(model.IgnoreLeadingTheForFileOrganization);
        // A new window still receives the unchanged saved preferences if reset is cancelled.
        Assert.Equal(original, new SettingsViewModel(original, new AudioBitratePreferences(320, 320, 320, 320)).ToOptions());
        Assert.True(model.Apply((options, policy, theme, validate, ignoreThe, bitrates) =>
        {
            Assert.Equal(new AudioBitratePreferences(), bitrates);
            Assert.Empty(options.CdrdaoExecutablePath);
            Assert.Equal(OverwritePolicy.Ask, policy);
            Assert.Equal(ApplicationTheme.System, theme);
            Assert.False(validate);
            Assert.False(ignoreThe);
        }));
        Assert.False(model.HasChanges);
        model.ResetToDefaults();
        Assert.False(model.HasChanges);
    }

    [Fact]
    public void CdrdaoPathIsTrackedValidatedAndPersisted()
    {
        string path = System.IO.Path.Combine(TestMediaFixture.NewFolder(), "cdrdao executable");
        System.IO.File.WriteAllText(path, "fixture");
        var model = new SettingsViewModel(new());
        model.CdrdaoExecutablePath = path;
        model.AutoCreateCueText = false;
        Assert.True(model.HasChanges);
        Assert.True(model.Apply((options, _, _, _) =>
        {
            var store = new SettingsStore(System.IO.Path.Combine(TestMediaFixture.NewFolder(), "settings.xml"));
            store.Save(options);
            Assert.Equal(path, store.Load().CdrdaoExecutablePath);
            Assert.False(store.Load().AutoCreateCueText);
        }));
        model.CdrdaoExecutablePath = path + "-missing";
        Assert.False(model.Apply((_, _, _, _) => throw new System.Exception("Must not save")));
        Assert.Contains("existing cdrdao executable", model.Error);
        model.CdrdaoExecutablePath = "";
        Assert.True(model.Apply((options, _, _, _) => Assert.Empty(options.CdrdaoExecutablePath)));
    }

    [Fact]
    public void ApplySavesAllPreferencesAndResetsDirtyState()
    {
        var model = new SettingsViewModel(new());
        Assert.False(model.HasChanges);
        model.Theme = (int)ApplicationTheme.Dark;
        model.ValidateBeforeConversion = true;
        model.RecurseFolders = !model.RecurseFolders;
        model.Overwrite = (int)OverwritePolicy.Ask;
        Assert.True(model.HasChanges);
        int saves = 0;
        Assert.True(model.Apply((options, policy, theme, validate) =>
        {
            saves++;
            Assert.Equal(model.RecurseFolders, options.RecurseFolders);
            Assert.Equal(OverwritePolicy.Ask, policy);
            Assert.Equal(ApplicationTheme.Dark, theme);
            Assert.True(validate);
        }));
        Assert.Equal(1, saves);
        Assert.False(model.HasChanges);
        model.Theme = (int)ApplicationTheme.Light;
        Assert.True(model.HasChanges);
        model.Theme = (int)ApplicationTheme.Dark;
        Assert.False(model.HasChanges);
        Assert.Equal(1, saves); // Unapplied edits never invoke persistence.
    }

    [Fact]
    public void FailedApplyKeepsEditsPendingAndSuccessfulRetryClearsError()
    {
        var model = new SettingsViewModel(new());
        model.ValidateBeforeConversion = true;
        Assert.False(model.Apply((_, _, _, _) => throw new System.IO.IOException("Disk unavailable")));
        Assert.True(model.HasChanges);
        Assert.Contains("Disk unavailable", model.Error);
        Assert.True(model.Apply((_, _, _, _) => { }));
        Assert.False(model.HasChanges);
        Assert.Empty(model.Error);
    }

    [Fact]
    public void LeadingTheToggleIsTrackedAndApplied()
    {
        var model = new SettingsViewModel(new());
        Assert.False(model.IgnoreLeadingTheForFileOrganization);
        model.IgnoreLeadingTheForFileOrganization = true;
        Assert.True(model.HasChanges);
        Assert.True(model.Apply((_, _, _, _, ignoreLeadingThe) => Assert.True(ignoreLeadingThe)));
        Assert.False(model.HasChanges);
    }
}
