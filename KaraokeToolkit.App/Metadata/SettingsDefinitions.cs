using KaraokeToolkit.App.Services;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Video;
using KaraokeToolkit.Core.Audio;
using KaraokeToolkit.Core.Archives;
using System;
using System.Collections.Generic;
namespace KaraokeToolkit.App.Metadata;

/// <summary>One source for setting labels, concise notes, detailed help and displayed choices.</summary>
public sealed class SettingsDefinitions
{
    public static SettingsDefinitions Instance { get; } = new();
    private SettingsDefinitions() { }
    public ChoiceSettingDefinition<string> Theme { get; } = new(
        "Theme", "Colour theme", "Colour theme",
        "Choose Light, Dark or System Default. System Default follows your operating system appearance.",
        SettingCategory.General, Array.AsReadOnly<string>(["System Default", "Light", "Dark"]));
    public ChoiceSettingDefinition<string> Backdrop { get; } = new(
        "Backdrop", "Window backdrop", "Effects depend on OS support; otherwise a solid background is used.",
        "On Windows 11, Automatic tries Mica, then Acrylic, then Solid. Elsewhere it uses Solid. Acrylic uses Dark with System Default. Select Light to override it. Explicit effects fall back to Solid when unavailable.",
        SettingCategory.General, Array.AsReadOnly<string>(["Automatic", "Solid", "Mica", "Acrylic"]));
    public SettingDefinition RecurseFolders { get; } = new(
        "RecurseFolders", "Recurse folders", "Recurse folders",
        "Search subfolders for operations that use this setting. Folder organization and flattening follow the scope described by their operation.",
        SettingCategory.General);
    public SettingDefinition IgnoreLeadingTheForFileOrganization { get; } = new(
        "IgnoreLeadingTheForFileOrganization", "Ignore leading 'The' when organizing files alphabetically", "Files only; names stay unchanged.",
        "Off by default. For example, 'The Beatles.mp3' goes to B instead of T when enabled. Filenames stay unchanged; folder organization always uses the full folder name.",
        SettingCategory.General);
    public SettingDefinition DeleteOriginals { get; } = new(
        "DeleteOldFiles", "Delete source files after successful conversion", "Delete source files after successful conversion",
        "Delete source files only after successful conversion. ZIP repacking replaces the original ZIP when enabled. Video creation always keeps its sources.",
        SettingCategory.General);
    public SettingDefinition ValidateBeforeConversion { get; } = new(
        "ValidateBeforeConversion", "Validate inputs before conversion", "Optional input checks. Outputs are checked automatically.",
        "Check each input group before converting; report issues without converting that group. Off by default to follow the original workflow. Output checks already run automatically where applicable: encoded audio readability and duration, BIN structure, and ZIP payload integrity. These checks do not verify visual lyrics or audio quality.",
        SettingCategory.General);
    public SettingDefinition AutoCreateCueText { get; } = new(
        "AutoCreateCueText", "Automatically generate CUE/TXT after conversion or ripping", "Shared by conversions and ripping.",
        "Generate CUE sheets for BIN tracks and TXT track listings after conversion or ripping. Turning this off does not disable the separate Create CUE/TXT Files operation.",
        SettingCategory.General);
    public ChoiceSettingDefinition<string> Overwrite { get; } = new(
        "OverwritePolicy", "Existing output files (including CUE/TXT)", "Ask prompts before replacing existing files.",
        "Choose how existing outputs are handled: keep and report a failure, skip, replace, or ask each time. This also applies to CUE/TXT files.",
        SettingCategory.General, Array.AsReadOnly<string>(["Fail — keep existing files", "Skip existing outputs", "Replace existing outputs", "Ask each time"]));
    public SettingDefinition AutoRenameZip { get; } = new(
        "AutoRenameZip", "Auto-rename ZIP files online", "Auto-rename ZIP files online",
        "Unavailable: online renaming is deferred. Local conversions ignore this saved legacy preference.",
        SettingCategory.Legacy);
    public SettingDefinition RenameBins { get; } = new(
        "RenameBins", "Rename BIN files online", "Rename BIN files online",
        "Unavailable: the original online database is no longer supported. Local conversions ignore this saved legacy preference.",
        SettingCategory.Legacy);
    public SettingDefinition NamingFormat { get; } = new(
        "NamingFormat", "Legacy naming format", "Legacy naming format",
        "Unavailable legacy naming template, retained for compatibility. It is not used by current local conversions.",
        SettingCategory.Legacy);
    public ChoiceSettingDefinition<string> Algorithm { get; } = new(
        "ZipAlgorithm", "ZIP algorithm:", "Deflate is recommended for legacy players.",
        "Choose the ZIP compression algorithm. Deflate is recommended for legacy karaoke players; BZip2 and LZMA may be incompatible.",
        SettingCategory.Archives, Array.AsReadOnly<string>(["Deflate (recommended)", "BZip2", "LZMA"]));
    public ChoiceSettingDefinition<string> CompressionLevel { get; } = new(
        "ZipCompression", "Deflate compression level:", "Levels 0–9 apply to Deflate only.",
        "Deflate only: 0 stores without compression, 5 balances speed and size, and 9 prioritizes smaller files. Other ZIP algorithms use their default settings.",
        SettingCategory.Archives, Array.AsReadOnly<string>(["0 - No compression", "1 - Fastest compression", "2", "3", "4", "5 - Balanced", "6", "7", "8", "9 - Smallest size"]));
    public ChoiceSettingDefinition<string> Encoder { get; } = new(
        "AudioEncoder", "Audio encoder:", "Audio encoder:",
        "Choose the audio format used by applicable conversions and disc ripping. WAV and FLAC are lossless; MP3 offers broad karaoke player compatibility.",
        SettingCategory.Audio, Array.AsReadOnly<string>(["OGG Vorbis", "MP3", "WMA 2", "WAV (PCM)", "FLAC", "AAC (M4A)"]));
    public ChoiceSettingDefinition<int> Bitrate { get; } = new(
        "AudioBitrate", "Bitrate (kbps):", "Bitrates are remembered per encoder. WAV and FLAC are lossless.",
        "Each MP3, OGG, WMA and AAC encoder remembers its own bitrate. WAV and FLAC are lossless and do not use this setting. MP3/Deflate ZIP has the broadest legacy karaoke compatibility.",
        SettingCategory.Audio, Array.AsReadOnly<int>([64, 96, 128, 160, 192, 224, 256, 320]));
    public SettingDefinition ExperimentalDiscRipping { get; } = new(
        "ExperimentalDiscRipping", "Enable experimental CD+G ripping", "Experimental; requires a compatible drive.",
        "Show CD+G ripping in the operation panel and enable its settings. Requires cdrdao and a compatible optical drive; physical disc reading remains experimental.",
        SettingCategory.DiscRipping);
    public SettingDefinition CdrdaoExecutablePath { get; } = new(
        "CdrdaoExecutablePath", "cdrdao executable (optional)", "Leave blank for automatic discovery.",
        "Choose the executable file, not its folder. Leave blank to use the bundled copy, then system PATH. Useful for Homebrew installations that Finder cannot locate. A saved path takes priority over the environment override.",
        SettingCategory.DiscRipping);
    public ChoiceSettingDefinition<string> VideoContainer { get; } = new(
        "VideoContainer", "Output format", "Letterboxed graphics. Original files are kept.",
        "Choose MP4, MKV or AVI for karaoke video output. MP4 is the default. The container determines the video and audio codecs. MP4 and MKV use H.264 video with AAC audio when available; AVI uses MPEG-4 video with MP3 audio. The graphics are letterboxed without stretching. Source karaoke files are kept.",
        SettingCategory.Video, Array.AsReadOnly<string>(["MP4 (default)", "MKV", "AVI"]));
    public ChoiceSettingDefinition<string> VideoResolution { get; } = new(
        "VideoResolution", "Resolution", "Resolution",
        "Choose the output video dimensions. CD+G graphics are letterboxed without stretching; higher resolution does not add detail to the original graphics.",
        SettingCategory.Video, Array.AsReadOnly<string>(["640 × 480", "1280 × 720 (default)", "1920 × 1080", "2560 × 1440", "3840 × 2160"]));
    public ChoiceSettingDefinition<string> VideoGraphicsSmoothing { get; } = new(
        "VideoGraphicsSmoothing", "Graphics smoothing", "Smooths pixel edges; does not recreate lyrics or add detail.",
        "None (default) preserves the original CD+G pixel edges. Bilinear softens edges, Bicubic uses cubic interpolation, and Lanczos uses a sharper resampling filter that can produce edge ringing. Smoothing changes scaling only; lyrics, colours and timing are kept. It does not recreate graphics or recover missing detail.",
        SettingCategory.Video, Array.AsReadOnly<string>(["None (default)", "Bilinear", "Bicubic", "Lanczos"]));
    public ChoiceSettingDefinition<int> VideoFrameRate { get; } = new(
        "VideoFrameRate", "Frames per second", "Frames per second",
        "Choose the output video frame rate. Higher rates create more frames; audio and CD+G graphics retain their original timeline.",
        SettingCategory.Video, Array.AsReadOnly<int>([25, 30, 60]));
    public ChoiceSettingDefinition<int> VideoBitrateKbps { get; } = new(
        "VideoBitrateKbps", "Video bitrate (kbps)", "Video bitrate (kbps)",
        "Set the video bitrate in kbps. Higher values generally produce larger files; they do not increase the detail in CD+G source graphics.",
        SettingCategory.Video, Array.AsReadOnly<int>([1000, 2000, 4000, 6000]));
    public SettingDefinition VideoAudioMatchSourceBitrate { get; } = new(
        "VideoAudioMatchSourceBitrate", "Match compressed source audio bitrate when possible", "Matches source bitrate when available; otherwise uses the fallback.",
        "Uses the source's average bitrate without rounding up, capped at 320 kbps. The fallback is used whenever the bitrate cannot be determined reliably, including lossless audio and missing metadata. Audio is re-encoded for the video container, so matching bitrate does not restore quality already lost.",
        SettingCategory.Video);
    public ChoiceSettingDefinition<int> VideoAudioBitrateKbps { get; } = new(
        "VideoAudioBitrateKbps", "Fallback audio bitrate (kbps)", "Fallback audio bitrate (kbps)",
        "Audio bitrate for video when source matching is off or a reliable source bitrate is unavailable, including lossless audio and missing metadata.",
        SettingCategory.Video, Array.AsReadOnly<int>([128, 192, 256, 320]));
    public HelpDefinition OnlineRenaming { get; } = new(
        "Online renaming is unavailable.", "Online renaming is deferred. Local conversions ignore these saved legacy preferences.");
    public HelpDefinition ExplorerOverview { get; } = new(
        "Open karaoke files and folders from their right-click menu.", "Add a right-click command for folders and supported karaoke files, for your Windows account. It opens the selection without starting a conversion. Default file associations are unchanged.");
    public HelpDefinition ExplorerLocation { get; } = new(
        "Windows 11: look under Show more options.", "On Windows 11, look under Show more options. The command uses this app's current location; enable it again after moving the app.");
    public HelpDefinition EnableExplorer { get; } = new(
        "Enable / update", "Register or update " + KaraokeToolkit.App.Services.ExplorerIntegration.MenuLabel + " for your Windows account at the current application location. Applies immediately.");
    public HelpDefinition RemoveExplorer { get; } = new("Remove", "Remove the Windows Explorer command for your account. Applies immediately.");
    public HelpDefinition ExplorerImmediate { get; } = new(
        "Applies immediately; no administrator access required.", "These buttons apply immediately and do not require administrator access.");
    public HelpDefinition BrowseCdrdao { get; } = new("Browse…", "Choose a local cdrdao executable file. Use Apply or OK to save the selected path.");
    public HelpDefinition RipSharedSettings { get; } = new(
        "Uses General, Audio Encoding and ZIP Compression settings.", "CUE/TXT generation and existing-file handling are configured in General. Audio and ZIP compression settings also apply to ripped tracks.");
    public HelpDefinition ResetDefaults { get; } = new(
        "Reset to Defaults…", "Ask for confirmation before resetting pending settings to defaults. Use Apply or OK to save the reset.");
    public HelpDefinition Save { get; } = new("OK", "Save pending settings and close this window.");
    public HelpDefinition Cancel { get; } = new("Cancel", "Close without saving pending changes. Settings already saved with Apply remain in effect.");
    public HelpDefinition Apply { get; } = new("Apply", "Save pending settings and keep this window open.");
    public IReadOnlyList<SettingDefinition> All => Array.AsReadOnly<SettingDefinition>([
        Theme, RecurseFolders, IgnoreLeadingTheForFileOrganization, DeleteOriginals, ValidateBeforeConversion, AutoCreateCueText, Overwrite, AutoRenameZip, RenameBins, NamingFormat, Algorithm, CompressionLevel, Encoder, Bitrate, ExperimentalDiscRipping, CdrdaoExecutablePath, VideoContainer, VideoResolution, VideoGraphicsSmoothing, VideoFrameRate, VideoBitrateKbps, VideoAudioMatchSourceBitrate, VideoAudioBitrateKbps
    ]);
}
