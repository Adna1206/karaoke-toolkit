using KaraokeToolkit.Core.Execution;
using KaraokeToolkit.Core.Video;
using KaraokeToolkit.Core.Audio;
using KaraokeToolkit.App.Features.Settings;
using KaraokeToolkit.App.Features.Main;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Recovery;
using KaraokeToolkit.Core.Settings;
using KaraokeToolkit.Core;
using Action = KaraokeToolkit.Core.Execution.OperationId;
namespace KaraokeToolkit.Tests.App.Settings;

public class PreferenceTests
{
    [Fact]
    public void VideoEncodingPreferencesRoundTripThroughOptionsAndSettings()
    {
        string path = Path.Combine(TestMediaFixture.NewFolder(), "settings.xmls");
        var main = new MainViewModel(path);
        var draft = new OptionsViewModel(main.Options)
        {
            VideoContainer = (int)VideoContainer.MKV,
            VideoResolution = (int)VideoResolution.FullHD,
            VideoFrameRate = 60,
            VideoBitrateKbps = 6000,
            VideoAudioMatchSourceBitrate = false,
            VideoAudioBitrateKbps = 256
        };
        Assert.True(draft.HasChanges);
        Assert.True(draft.Apply((options, policy, theme, validate, ignoreThe, bitrates) =>
            main.SaveOptions(options, policy, theme, validate, ignoreThe, bitrates)));
        var reopened = new MainViewModel(path);
        Assert.Equal(VideoContainer.MKV, reopened.Options.VideoContainer);
        Assert.Equal(VideoResolution.FullHD, reopened.Options.VideoResolution);
        Assert.Equal(60, reopened.Options.VideoFrameRate);
        Assert.Equal(6000, reopened.Options.VideoBitrateKbps);
        Assert.False(reopened.Options.VideoAudioMatchSourceBitrate);
        Assert.Equal(256, reopened.Options.VideoAudioBitrateKbps);
    }

    [Theory]
    [InlineData(ApplicationTheme.System)]
    [InlineData(ApplicationTheme.Light)]
    [InlineData(ApplicationTheme.Dark)]
    public void ThemeGenerationAndOverwritePersistTogether(ApplicationTheme theme)
    {
        string path=Path.Combine(TestMediaFixture.NewFolder(),"settings.xmls");
        var model=new MainViewModel(path);
        ApplicationTheme? applied=null;model.ThemeChanged+=value=>applied=value;
        model.SaveOptions(model.Options with{AutoCreateCueText=false},OverwritePolicy.Ask,theme);
        var reopened=new MainViewModel(path);
        Assert.Equal(theme,reopened.Theme);Assert.Equal(theme,applied);
        Assert.False(reopened.Options.AutoCreateCueText);Assert.Equal(OverwritePolicy.Ask,reopened.OverwritePolicy);
        var draft=new OptionsViewModel(reopened.Options){Theme=(int)ApplicationTheme.Dark,Overwrite=(int)OverwritePolicy.Replace,AutoCreateCueText=true};
        Assert.Equal(theme,new MainViewModel(path).Theme);Assert.False(new MainViewModel(path).Options.AutoCreateCueText);
    }
    [Fact] public void LegacySettingsDefaultToSystemThemeAndAsk()
    {
        string path=Path.Combine(TestMediaFixture.NewFolder(),"settings.xmls");new SettingsStore(path).Save(new());
        var store=new ApplicationSettingsStore(path);Assert.Equal(ApplicationTheme.System,store.LoadTheme());Assert.Equal(OverwritePolicy.Ask,store.LoadOverwritePolicy());
    }
    [Fact] public void InvalidThemeDoesNotOverwriteSettings()
    {
        string path=Path.Combine(TestMediaFixture.NewFolder(),"settings.xmls");var store=new ApplicationSettingsStore(path);store.Save(new());string before=File.ReadAllText(path);
        Assert.Throws<ArgumentOutOfRangeException>(()=>store.Save(new(),theme:(ApplicationTheme)99));Assert.Equal(before,File.ReadAllText(path));
    }
    [Fact]
    public void AudioEncodersRememberSeparateBitratesAcrossSwitchesAndLaunches()
    {
        string path=Path.Combine(TestMediaFixture.NewFolder(),"settings.xmls");
        var main=new MainViewModel(path);
        var draft=new OptionsViewModel(main.Options,main.AudioBitrates);
        draft.Encoder=(int)AudioEncoder.MP3;draft.Bitrate=192;
        draft.Encoder=(int)AudioEncoder.OGG;draft.Bitrate=96;
        draft.Encoder=(int)AudioEncoder.WMA;draft.Bitrate=160;
        draft.Encoder=(int)AudioEncoder.AAC;draft.Bitrate=224;
        draft.Encoder=(int)AudioEncoder.WAV;
        Assert.False(draft.UsesAudioBitrate);
        draft.Encoder=(int)AudioEncoder.MP3;Assert.Equal(192,draft.Bitrate);
        draft.Encoder=(int)AudioEncoder.OGG;Assert.Equal(96,draft.Bitrate);
        draft.Encoder=(int)AudioEncoder.WMA;Assert.Equal(160,draft.Bitrate);
        draft.Encoder=(int)AudioEncoder.AAC;Assert.Equal(224,draft.Bitrate);
        Assert.True(draft.Apply((options,policy,theme,validate,ignoreThe,bitrates) =>
            main.SaveOptions(options,policy,theme,validate,ignoreThe,bitrates)));

        var reopened=new MainViewModel(path);
        Assert.Equal(new AudioBitratePreferences(192,96,160,224),reopened.AudioBitrates);
        var next=new OptionsViewModel(reopened.Options,reopened.AudioBitrates);
        next.Encoder=(int)AudioEncoder.MP3;Assert.Equal(192,next.Bitrate);
        next.Encoder=(int)AudioEncoder.OGG;Assert.Equal(96,next.Bitrate);
        next.Encoder=(int)AudioEncoder.WMA;Assert.Equal(160,next.Bitrate);
        next.Encoder=(int)AudioEncoder.AAC;Assert.Equal(224,next.Bitrate);
    }
    [Fact]
    public void ExistingSettingsMigrateCurrentEncoderBitrate()
    {
        string path=Path.Combine(TestMediaFixture.NewFolder(),"settings.xmls");
        new SettingsStore(path).Save(new ConversionOptions {AudioEncoder=AudioEncoder.OGG,AudioBitrate=224});
        var reopened=new MainViewModel(path);
        Assert.Equal(224,reopened.AudioBitrates.Ogg);
        Assert.Equal(128,reopened.AudioBitrates.Mp3);
        Assert.Equal(128,reopened.AudioBitrates.Wma);
        Assert.Equal(128,reopened.AudioBitrates.Aac);
    }
}
