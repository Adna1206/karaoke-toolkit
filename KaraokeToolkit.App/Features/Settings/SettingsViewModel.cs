using KaraokeToolkit.Core.Execution;
using KaraokeToolkit.Core.Video;
using KaraokeToolkit.Core.Audio;
using KaraokeToolkit.Core.Archives;
using KaraokeToolkit.App.Shared;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.App.Metadata;
using KaraokeToolkit.Core.Settings;
using System;
using System.ComponentModel;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KaraokeToolkit.Core;
using KaraokeToolkit.Core.FileOperations;
namespace KaraokeToolkit.App.Features.Settings;

public partial class SettingsViewModel : ViewModelBase
{
    public SettingsDefinitions Definitions => SettingsDefinitions.Instance;
    private readonly ConversionOptions initial;
    private AudioBitratePreferences audioBitrates = new();
    private bool initializing;
    private sealed record Snapshot(ConversionOptions Options, int Overwrite, int Theme, bool Validate, bool IgnoreLeadingThe,
        AudioBitratePreferences AudioBitrates, int Backdrop, bool RememberInputFolder, int RecoveryVerification, bool SaveResumableRun,
        bool CheckForUpdatesOnStartup, bool CheckForInterruptedRunsOnStartup, int CheckTiming, int MoveRecoveryVerification);
    private Snapshot? applied;
    private Snapshot Current => new(ToOptions(), Overwrite, Theme, ValidateBeforeConversion, IgnoreLeadingTheForFileOrganization,
        audioBitrates, Backdrop, RememberInputFolder, RecoveryVerification, SaveResumableRun, CheckForUpdatesOnStartup, CheckForInterruptedRunsOnStartup,
        CheckTiming, MoveRecoveryVerification);
    public bool HasChanges => applied != null && Current != applied;

    public void MarkApplied()
    {
        applied = Current;
        OnPropertyChanged(nameof(HasChanges));
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is not (nameof(HasChanges) or nameof(Error)))
            OnPropertyChanged(nameof(HasChanges));
    }

    public bool Apply(Action<ConversionOptions, OverwritePolicy, ApplicationTheme, bool, bool, AudioBitratePreferences, ApplicationBackdrop> save)
    {
        try
        {
            var options = ToOptions();
            if (!string.IsNullOrWhiteSpace(options.CdrdaoExecutablePath) &&
                (!System.IO.Path.IsPathFullyQualified(options.CdrdaoExecutablePath) || !System.IO.File.Exists(options.CdrdaoExecutablePath)))
                throw new System.IO.FileNotFoundException("Choose an existing cdrdao executable using its full path.");
            new ConversionRequest(OperationId.AudioGToZip, ".", options).Validate();
            audioBitrates.Validate();
            if (!Enum.IsDefined((ApplicationBackdrop)Backdrop)) throw new ArgumentOutOfRangeException(nameof(Backdrop));
            save(options, (OverwritePolicy)Overwrite, (ApplicationTheme)Theme, ValidateBeforeConversion,
                IgnoreLeadingTheForFileOrganization, audioBitrates, (ApplicationBackdrop)Backdrop);
            Error = "";
            MarkApplied();
            return true;
        }
        catch (Exception error)
        {
            Error = "Could not save options: " + error.Message;
            return false;
        }
    }

    public bool Apply(Action<ConversionOptions, OverwritePolicy, ApplicationTheme, bool, bool, AudioBitratePreferences> save) =>
        Apply((options, policy, theme, validate, ignoreThe, bitrates, _) => save(options, policy, theme, validate, ignoreThe, bitrates));

    public bool Apply(Action<ConversionOptions, OverwritePolicy, ApplicationTheme, bool, bool> save) =>
        Apply((options, policy, theme, validate, ignoreThe, _) => save(options, policy, theme, validate, ignoreThe));

    public bool Apply(Action<ConversionOptions, OverwritePolicy, ApplicationTheme, bool> save) =>
        Apply((options, policy, theme, validate, _, _) => save(options, policy, theme, validate));

    [ObservableProperty]
    public partial bool ValidateBeforeConversion
    {
        get; set;
    }
    [ObservableProperty] public partial bool IgnoreLeadingTheForFileOrganization { get; set; }
    [ObservableProperty] public partial int RecoveryVerification { get; set; } = (int)OperationCapabilities.DefaultFileWriteRecoveryVerification;
    [ObservableProperty] public partial int MoveRecoveryVerification { get; set; } = (int)OperationCapabilities.DefaultFileMoveRecoveryVerification;
    [ObservableProperty] public partial bool SaveResumableRun { get; set; } = true;
    [ObservableProperty] public partial bool CheckForUpdatesOnStartup { get; set; }
    [ObservableProperty] public partial bool CheckForInterruptedRunsOnStartup { get; set; } = true;
    [ObservableProperty] public partial int CheckTiming { get; set; } = (int)OperationCapabilities.DefaultFileMoveCheckTiming;
    [ObservableProperty] public partial bool RememberInputFolder { get; set; }
    [ObservableProperty] public partial bool ExperimentalDiscRipping { get; set; }
    [ObservableProperty] public partial string CdrdaoExecutablePath { get; set; } = "";
    public IReadOnlyList<string> Themes => Definitions.Theme.Choices;
    public IReadOnlyList<string> Backdrops => Definitions.Backdrop.Choices;
    [ObservableProperty] public partial int Backdrop { get; set; }
    [ObservableProperty]
    public partial int Theme
    {
        get; set;
    }
    public IReadOnlyList<string> OverwritePolicies => Definitions.Overwrite.Choices;
    [ObservableProperty]
    public partial int Overwrite
    {
        get; set;
    }
    public IReadOnlyList<string> Encoders => Definitions.Encoder.Choices;
    private static bool UsesBitrate(AudioEncoder encoder) => encoder is AudioEncoder.OGG or AudioEncoder.MP3 or AudioEncoder.WMA or AudioEncoder.AAC;
    public bool UsesAudioBitrate => UsesBitrate((AudioEncoder)Encoder);
    public IReadOnlyList<int> Bitrates => Definitions.Bitrate.Choices;
    public IReadOnlyList<string> VideoContainers => Definitions.VideoContainer.Choices;
    public IReadOnlyList<string> VideoResolutions => Definitions.VideoResolution.Choices;
    public IReadOnlyList<string> VideoGraphicsSmoothingOptions => Definitions.VideoGraphicsSmoothing.Choices;
    public IReadOnlyList<int> VideoFrameRates => Definitions.VideoFrameRate.Choices;
    public IReadOnlyList<int> VideoBitrates => Definitions.VideoBitrateKbps.Choices;
    public IReadOnlyList<int> VideoAudioBitrates => Definitions.VideoAudioBitrateKbps.Choices;
    [ObservableProperty] public partial int VideoContainer { get; set; }
    [ObservableProperty] public partial int VideoResolution { get; set; }
    [ObservableProperty] public partial int VideoGraphicsSmoothing { get; set; }
    [ObservableProperty] public partial int VideoFrameRate { get; set; }
    [ObservableProperty] public partial int VideoBitrateKbps { get; set; }
    [ObservableProperty] public partial bool VideoAudioMatchSourceBitrate { get; set; }
    [ObservableProperty] public partial int VideoAudioBitrateKbps { get; set; }
    public IReadOnlyList<string> ZipAlgorithms => Definitions.Algorithm.Choices;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDeflate))]
    public partial int Algorithm { get; set; }
    public bool IsDeflate => Algorithm == (int)ZipAlgorithm.Deflate;
    public IReadOnlyList<string> CompressionLevels => Definitions.CompressionLevel.Choices;
    [ObservableProperty]
    public partial bool RecurseFolders
    {
        get; set;
    }
    [ObservableProperty]
    public partial bool DeleteOriginals
    {
        get; set;
    }
    [ObservableProperty]
    public partial bool AutoCreateCueText
    {
        get; set;
    }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TrackListingPreview))]
    public partial string TrackListingName { get; set; } = MetadataFileNames.DefaultTemplate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CueSheetPreview))]
    public partial string CueSheetName { get; set; } = MetadataFileNames.DefaultTemplate;

    public string TrackListingPreview => PreviewName(TrackListingName, ".txt");
    public string CueSheetPreview => PreviewName(CueSheetName, ".cue");

    private static string PreviewName(string? template, string extension)
    {
        try { return "Example: " + MetadataFileNames.Resolve(template, "Party Mix", extension); }
        catch (ArgumentException error) { return error.Message; }
    }

    [RelayCommand]
    private void ResetOutputNames()
    {
        TrackListingName = MetadataFileNames.DefaultTemplate;
        CueSheetName = MetadataFileNames.DefaultTemplate;
    }
    [ObservableProperty]
    public partial bool AutoRenameZip
    {
        get; set;
    }
    [ObservableProperty]
    public partial bool RenameBins
    {
        get; set;
    }
    [ObservableProperty] public partial string NamingFormat { get; set; } = "";
    [ObservableProperty]
    public partial int CompressionLevel
    {
        get; set;
    }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UsesAudioBitrate))]
    public partial int Encoder
    {
        get; set;
    }
    partial void OnEncoderChanged(int oldValue, int newValue)
    {
        if (initializing) return;
        audioBitrates = audioBitrates.With((AudioEncoder)oldValue, Bitrate);
        if (UsesBitrate((AudioEncoder)newValue))
            Bitrate = audioBitrates.For((AudioEncoder)newValue);
    }
    [ObservableProperty]
    public partial int Bitrate
    {
        get; set;
    }
    partial void OnBitrateChanged(int value)
    {
        if (!initializing)
            audioBitrates = audioBitrates.With((AudioEncoder)Encoder, value);
    }
    [ObservableProperty] public partial string Error { get; set; } = "";
    [ObservableProperty] public partial StatusKind StatusSeverity { get; private set; } = StatusKind.Failure;

    partial void OnErrorChanged(string value) => StatusSeverity = StatusKind.Failure;

    internal void ShowStatus(string message, StatusKind kind)
    {
        Error = message;
        StatusSeverity = kind;
    }
    public SettingsViewModel() : this(new()) { }
    public SettingsViewModel(ConversionOptions options, AudioBitratePreferences? savedBitrates = null)
    {
        initial = options;
        LoadEditablePreferences(options, savedBitrates ?? new AudioBitratePreferences().With(options.AudioEncoder, options.AudioBitrate));
        MarkApplied();
    }

    /// <summary>Resets pending preferences without saving or changing Explorer registration.</summary>
    public void ResetToDefaults()
    {
        LoadEditablePreferences(new ConversionOptions(), new AudioBitratePreferences());
        Theme = (int)ApplicationTheme.System;
        Backdrop = (int)ApplicationBackdrop.Automatic;
        Overwrite = (int)OverwritePolicy.Ask;
        ValidateBeforeConversion = false;
        IgnoreLeadingTheForFileOrganization = false;
        RecoveryVerification = (int)OperationCapabilities.DefaultFileWriteRecoveryVerification;
        MoveRecoveryVerification = (int)OperationCapabilities.DefaultFileMoveRecoveryVerification;
        SaveResumableRun = true;
        CheckForInterruptedRunsOnStartup = true;
        CheckTiming = (int)OperationCapabilities.DefaultFileMoveCheckTiming;
        CheckForUpdatesOnStartup = false;
        RememberInputFolder = false;
        Error = "";
        OnPropertyChanged(nameof(HasChanges));
    }

    private void LoadEditablePreferences(ConversionOptions options, AudioBitratePreferences bitrates)
    {
        initializing = true;
        audioBitrates = bitrates;
        ExperimentalDiscRipping = options.ExperimentalDiscRipping;
        CdrdaoExecutablePath = options.CdrdaoExecutablePath;
        RecurseFolders = options.RecurseFolders;
        DeleteOriginals = options.DeleteOldFiles;
        AutoCreateCueText = options.AutoCreateCueText;
        TrackListingName = options.TrackListingName;
        CueSheetName = options.CueSheetName;
        AutoRenameZip = options.AutoRenameZip;
        RenameBins = options.RenameBins;
        NamingFormat = options.NamingFormat;
        CompressionLevel = (int)options.ZipCompression;
        Algorithm = (int)options.ZipAlgorithm;
        Encoder = (int)options.AudioEncoder;
        Bitrate = UsesBitrate(options.AudioEncoder) ? audioBitrates.For(options.AudioEncoder) : options.AudioBitrate;
        VideoContainer = (int)options.VideoContainer;
        VideoResolution = (int)options.VideoResolution;
        VideoGraphicsSmoothing = (int)options.VideoGraphicsSmoothing;
        VideoFrameRate = options.VideoFrameRate;
        VideoBitrateKbps = options.VideoBitrateKbps;
        VideoAudioMatchSourceBitrate = options.VideoAudioMatchSourceBitrate;
        VideoAudioBitrateKbps = options.VideoAudioBitrateKbps;
        initializing = false;
    }
    public ConversionOptions ToOptions() => initial with
    {
        ExperimentalDiscRipping = ExperimentalDiscRipping,
        CdrdaoExecutablePath = CdrdaoExecutablePath.Trim(),
        RecurseFolders = RecurseFolders,
        DeleteOldFiles = DeleteOriginals,
        AutoCreateCueText = AutoCreateCueText,
        TrackListingName = TrackListingName?.Trim() ?? "",
        CueSheetName = CueSheetName?.Trim() ?? "",
        AutoRenameZip = AutoRenameZip,
        RenameBins = RenameBins,
        NamingFormat = NamingFormat,
        ZipCompression = (ZipCompression)CompressionLevel,
        ZipAlgorithm = (ZipAlgorithm)Algorithm,
        AudioEncoder = (AudioEncoder)Encoder,
        AudioBitrate = Bitrate,
        VideoContainer = (VideoContainer)VideoContainer,
        VideoResolution = (VideoResolution)VideoResolution,
        VideoGraphicsSmoothing = (VideoGraphicsSmoothing)VideoGraphicsSmoothing,
        VideoFrameRate = VideoFrameRate,
        VideoBitrateKbps = VideoBitrateKbps,
        VideoAudioMatchSourceBitrate = VideoAudioMatchSourceBitrate,
        VideoAudioBitrateKbps = VideoAudioBitrateKbps
    };
}


