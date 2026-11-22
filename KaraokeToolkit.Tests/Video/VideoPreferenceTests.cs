using System.Text.Json;
using System.Xml.Linq;
using KaraokeToolkit.App.Features.Settings;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Settings;
using KaraokeToolkit.Core.Video;

namespace KaraokeToolkit.Tests.Video;

public class VideoPreferenceTests
{
    [Theory]
    [InlineData(VideoGraphicsSmoothing.None, VideoResolution.HD)]
    [InlineData(VideoGraphicsSmoothing.Bilinear, VideoResolution.FullHD)]
    [InlineData(VideoGraphicsSmoothing.Bicubic, VideoResolution.QHD)]
    [InlineData(VideoGraphicsSmoothing.Lanczos, VideoResolution.UHD)]
    public void AppliedChoicesSurviveSettingsAndRecoverySerialization(VideoGraphicsSmoothing smoothing, VideoResolution resolution)
    {
        string path = Path.Combine(TestMediaFixture.NewFolder(), "settings.xml");
        var store = new ApplicationSettingsStore(path);
        var draft = new SettingsViewModel(store.Load())
        {
            VideoGraphicsSmoothing = (int)smoothing, VideoResolution = (int)resolution
        };
        Assert.True(draft.Apply((options, policy, theme, validate, ignoreThe, bitrates) =>
            store.Save(options, policy, theme, validate, ignoreThe, bitrates)));
        var loaded = new ApplicationSettingsStore(path).Load();
        Assert.Equal(smoothing, loaded.VideoGraphicsSmoothing);
        Assert.Equal(resolution, loaded.VideoResolution);
        Assert.Equal(loaded, JsonSerializer.Deserialize<ConversionOptions>(JsonSerializer.Serialize(loaded)));
        Assert.Contains(XDocument.Load(path).Root!.Element("Processing")!.Elements("Setting"),
            entry => (string?)entry.Attribute("id") == "video.graphics-smoothing");
    }

    [Fact]
    public void MissingSmoothingAndResetDefaultsPreserveUnsmoothedHdRendering()
    {
        string path = Path.Combine(TestMediaFixture.NewFolder(), "settings.xml");
        File.WriteAllText(path, "<KaraokeToolkit schemaVersion='1'><Processing><Setting id='video.resolution'>HD</Setting></Processing></KaraokeToolkit>");
        var loaded = new SettingsStore(path).Load();
        Assert.Equal(VideoGraphicsSmoothing.None, loaded.VideoGraphicsSmoothing);
        Assert.Equal("neighbor", VideoOptions.ScalingAlgorithm(loaded.VideoGraphicsSmoothing));
        var draft = new SettingsViewModel(loaded) { VideoGraphicsSmoothing = (int)VideoGraphicsSmoothing.Lanczos };
        draft.ResetToDefaults();
        Assert.Equal(VideoGraphicsSmoothing.None, draft.ToOptions().VideoGraphicsSmoothing);
        Assert.Equal(VideoResolution.HD, draft.ToOptions().VideoResolution);
    }

    [Fact]
    public void InvalidSmoothingIsRejectedOnSaveAndDefaultsOnLoad()
    {
        string path = Path.Combine(TestMediaFixture.NewFolder(), "settings.xml");
        var store = new SettingsStore(path);
        store.Save(new());
        string before = File.ReadAllText(path);
        Assert.Throws<ArgumentOutOfRangeException>(() => store.Save(new() { VideoGraphicsSmoothing = (VideoGraphicsSmoothing)99 }));
        Assert.Equal(before, File.ReadAllText(path));
        var xml = XDocument.Load(path);
        xml.Root!.Element("Processing")!.Elements("Setting")
            .Single(field => (string?)field.Attribute("id") == "video.graphics-smoothing").Value = "Unsupported";
        xml.Save(path);
        Assert.Equal(VideoGraphicsSmoothing.None, store.Load().VideoGraphicsSmoothing);
    }

}
