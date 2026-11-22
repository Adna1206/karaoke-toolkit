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
    public void StartupUpdateCheckDefaultsOffAndPersistsThroughSettings()
    {
        string path = Path.Combine(TestMediaFixture.NewFolder(), "settings.xml");
        var model = new MainViewModel(path);
        Assert.False(model.CheckForUpdatesOnStartup);

        model.SaveOptions(model.Options, model.OverwritePolicy, model.Theme, false, false,
            model.AudioBitrates, model.Backdrop, false, checkForUpdatesOnStartup: true);
        model.SaveOptions(model.Options);

        var reopened = new MainViewModel(path);
        Assert.True(reopened.CheckForUpdatesOnStartup);
        var draft = new SettingsViewModel(reopened.Options) { CheckForUpdatesOnStartup = true };
        draft.MarkApplied();
        draft.ResetToDefaults();
        Assert.False(draft.CheckForUpdatesOnStartup);
        Assert.True(draft.HasChanges);
    }

    [Fact]
    public void PickerRemembersOnlyAcceptedInputsAndDisablingClearsTheSavedFolder()
    {
        string path = Path.Combine(TestMediaFixture.NewFolder(), "options.xmls");
        string folder = TestMediaFixture.NewFolder();
        string file = Path.Combine(folder, "song.zip");
        File.WriteAllText(file, "picker fixture");
        var model = new MainViewModel(path);
        model.SelectedOperation = model.Operations.Single(operation => operation.Action == Action.ZipToAudioG);
        model.AddPickedInputs([file]);
        Assert.Null(model.InputPickerStartFolder);

        model.SaveOptions(model.Options, model.OverwritePolicy, model.Theme, false, false, model.AudioBitrates, model.Backdrop, true);
        model.AddPickedInputs([file]);
        var reopened = new MainViewModel(path);
        Assert.Equal(folder, reopened.InputPickerStartFolder);
        Assert.Empty(reopened.SelectedInputs);
        Assert.Throws<FileNotFoundException>(() => reopened.AddPickedInputs([Path.Combine(folder, "missing.zip")]));
        Assert.Equal(folder, reopened.InputPickerStartFolder);

        reopened.SaveOptions(reopened.Options, reopened.OverwritePolicy, reopened.Theme, false, false, reopened.AudioBitrates, reopened.Backdrop, false);
        Assert.Equal((false, (string?)null), new ApplicationSettingsStore(path).LoadInputFolderPreference());
        Assert.DoesNotContain("LastInputFolder", File.ReadAllText(path));
    }

    [Fact]
    public void OutputNamePreferencesPreviewPersistAndRejectUnsafeNames()
    {
        string path = Path.Combine(TestMediaFixture.NewFolder(), "settings.xmls");
        var store = new ApplicationSettingsStore(path);
        var draft = new SettingsViewModel(store.Load()) { TrackListingName = "{folder} - tracks", CueSheetName = "Disc" };
        Assert.Equal("Example: Party Mix - tracks.txt", draft.TrackListingPreview);
        Assert.Equal("Example: Disc.cue", draft.CueSheetPreview);
        Assert.True(draft.Apply((options, _, _, _) => store.Save(options)), draft.Error);
        Assert.Equal("{folder} - tracks", store.Load().TrackListingName);
        Assert.Equal("Disc", store.Load().CueSheetName);

        draft.CueSheetName = "../outside";
        Assert.False(draft.Apply((options, _, _, _) => store.Save(options)));
        Assert.Equal("Disc", store.Load().CueSheetName);

        draft.ResetOutputNamesCommand.Execute(null);
        Assert.Equal("Example: Party Mix.txt", draft.TrackListingPreview);
        Assert.Equal("Example: Party Mix.cue", draft.CueSheetPreview);
    }

    [Fact]
    public void UnsupportedSavedValuesUseDefaultsWithoutLosingOtherPreferences()
    {
        string path = Path.Combine(TestMediaFixture.NewFolder(), "settings.xmls");
        File.WriteAllText(path, """
            <KaraokeToolkit schemaVersion="1">
              <Processing>
                <Setting id="audio.encoder">999</Setting>
                <Setting id="metadata.create-cue-text">unknown</Setting>
                <Setting id="files.recurse">false</Setting>
                <Setting id="future.option">anything</Setting>
                <Setting id="zip.level">future</Setting>
                <Setting id="video.resolution">future</Setting>
                <Setting id="video.frame-rate">120</Setting>
                <Setting id="video.bitrate">6000</Setting>
                <Setting id="audio.mp3-bitrate">999</Setting>
                <Setting id="audio.ogg-bitrate">128</Setting>
                <Setting id="files.overwrite">future</Setting>
              </Processing>
              <Desktop>
                <Theme>future</Theme>
                <Backdrop>Solid</Backdrop>
                <FavouriteOperations><Operation>AudioGToZip</Operation></FavouriteOperations>
              </Desktop>
            </KaraokeToolkit>
            """);

        var model = new MainViewModel(path);
        var defaults = new ConversionOptions();

        Assert.Equal(defaults.AudioEncoder, model.Options.AudioEncoder);
        Assert.Equal(defaults.AutoCreateCueText, model.Options.AutoCreateCueText);
        Assert.False(model.Options.RecurseFolders);
        Assert.Equal(defaults.ZipCompression, model.Options.ZipCompression);
        Assert.Equal(defaults.VideoResolution, model.Options.VideoResolution);
        Assert.Equal(defaults.VideoFrameRate, model.Options.VideoFrameRate);
        Assert.Equal(6000, model.Options.VideoBitrateKbps);
        Assert.Equal(ConversionOptions.DefaultAudioBitrate, model.AudioBitrates.Mp3);
        Assert.Equal(128, model.AudioBitrates.Ogg);
        Assert.Equal(OverwritePolicy.Ask, model.OverwritePolicy);
        Assert.Equal(ApplicationTheme.System, model.Theme);
        Assert.Equal(ApplicationBackdrop.Solid, model.Backdrop);
        Assert.Contains(model.FavouriteOperations, choice => choice.Action == Action.AudioGToZip);
    }

    [Fact]
    public void VideoEncodingPreferencesRoundTripThroughOptionsAndSettings()
    {
        string path = Path.Combine(TestMediaFixture.NewFolder(), "settings.xmls");
        var main = new MainViewModel(path);
        var draft = new SettingsViewModel(main.Options)
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
        var draft=new SettingsViewModel(reopened.Options){Theme=(int)ApplicationTheme.Dark,Overwrite=(int)OverwritePolicy.Replace,AutoCreateCueText=true};
        Assert.Equal(theme,new MainViewModel(path).Theme);Assert.False(new MainViewModel(path).Options.AutoCreateCueText);
    }
    [Fact] public void MissingPreferencesDefaultToSystemThemeAndAsk()
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
        var draft=new SettingsViewModel(main.Options,main.AudioBitrates);
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
        var next=new SettingsViewModel(reopened.Options,reopened.AudioBitrates);
        next.Encoder=(int)AudioEncoder.MP3;Assert.Equal(192,next.Bitrate);
        next.Encoder=(int)AudioEncoder.OGG;Assert.Equal(96,next.Bitrate);
        next.Encoder=(int)AudioEncoder.WMA;Assert.Equal(160,next.Bitrate);
        next.Encoder=(int)AudioEncoder.AAC;Assert.Equal(224,next.Bitrate);
    }
    [Fact]
    public void CurrentEncoderBitrateIsSharedWithDesktop()
    {
        string path=Path.Combine(TestMediaFixture.NewFolder(),"settings.xmls");
        new SettingsStore(path).Save(new ConversionOptions {AudioEncoder=AudioEncoder.OGG,AudioBitrate=224});
        var reopened=new MainViewModel(path);
        Assert.Equal(224,reopened.AudioBitrates.Ogg);
        Assert.Equal(320,reopened.AudioBitrates.Mp3);
        Assert.Equal(320,reopened.AudioBitrates.Wma);
        Assert.Equal(320,reopened.AudioBitrates.Aac);
    }
}
