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
        "System Default follows your operating system's light or dark theme.",
        SettingCategory.General, Array.AsReadOnly<string>(["System Default", "Light", "Dark"]));
    public ChoiceSettingDefinition<string> Backdrop { get; } = new(
        "Backdrop", "Window backdrop", "Unavailable effects fall back to Solid.",
        "On Windows 11, Automatic tries Mica, Acrylic, then Solid. Elsewhere it uses Solid. Acrylic uses Dark with System Default. Choose Light to override it.",
        SettingCategory.General, Array.AsReadOnly<string>(["Automatic", "Solid", "Mica", "Acrylic"]));
    public SettingDefinition RecurseFolders { get; } = new(
        "RecurseFolders", "Recurse folders", "Recurse folders",
        "Include subfolders where supported. Folder organization and flattening use their own scope.",
        SettingCategory.General);
    public SettingDefinition IgnoreLeadingTheForFileOrganization { get; } = new(
        "IgnoreLeadingTheForFileOrganization", "Ignore leading 'The' when organizing files alphabetically", "Sorts 'The Beatles' under B and keeps the filename.",
        "Sort files such as 'The Beatles.mp3' under B. Folder sorting uses the full folder name.",
        SettingCategory.General);
    public SettingDefinition DeleteOriginals { get; } = new(
        "DeleteOldFiles", "Delete source files after successful conversion", "Delete source files after successful conversion",
        "Remove sources after a successful conversion. ZIP repacking replaces the original. Video creation keeps its sources.",
        SettingCategory.General);
    public SettingDefinition ValidateBeforeConversion { get; } = new(
        "ValidateBeforeConversion", "Validate inputs before conversion", "Skip problem inputs. Output checks run automatically.",
        "Check each track before conversion and report any skipped inputs. Automatic output checks cover file integrity, audio readability and duration where applicable.",
        SettingCategory.General);
    public SettingDefinition AutoCreateCueText { get; } = new(
        "AutoCreateCueText", "Automatically generate CUE/TXT after conversion or ripping", "Creates BIN cue sheets and TXT track lists.",
        "Create track lists and BIN cue sheets after supported conversions or rips. Create CUE/TXT Files is also available on its own.",
        SettingCategory.General);
    public ChoiceSettingDefinition<string> Overwrite { get; } = new(
        "OverwritePolicy", "Existing output files (including CUE/TXT)", "Ask prompts before replacing existing files.",
        "Keep and report a failure, skip, replace, or ask when an output already exists. Includes CUE and TXT files.",
        SettingCategory.General, Array.AsReadOnly<string>(["Fail — keep existing files", "Skip existing outputs", "Replace existing outputs", "Ask each time"]));
    public SettingDefinition AutoRenameZip { get; } = new(
        "AutoRenameZip", "Auto-rename ZIP files online", "Auto-rename ZIP files online",
        "Saved for older setups. Online ZIP renaming is unavailable.",
        SettingCategory.Legacy);
    public SettingDefinition RenameBins { get; } = new(
        "RenameBins", "Rename BIN files online", "Rename BIN files online",
        "Saved for older setups. The online BIN database has closed.",
        SettingCategory.Legacy);
    public SettingDefinition NamingFormat { get; } = new(
        "NamingFormat", "Legacy naming format", "Legacy naming format",
        "Naming template saved for older setups.",
        SettingCategory.Legacy);
    public ChoiceSettingDefinition<string> Algorithm { get; } = new(
        "ZipAlgorithm", "ZIP algorithm:", "Deflate is recommended for legacy players.",
        "Choose how ZIPs are compressed. Use Deflate for the widest support on older karaoke players.",
        SettingCategory.Archives, Array.AsReadOnly<string>(["Deflate (recommended)", "BZip2", "LZMA"]));
    public ChoiceSettingDefinition<string> CompressionLevel { get; } = new(
        "ZipCompression", "Deflate compression level:", "Levels 0–9 apply to Deflate only.",
        "For Deflate, 0 stores files as-is, 5 balances speed and size, and 9 prioritizes size. Other algorithms use their defaults.",
        SettingCategory.Archives, Array.AsReadOnly<string>(["0 - No compression", "1 - Fastest compression", "2", "3", "4", "5 - Balanced", "6", "7", "8", "9 - Smallest size"]));
    public ChoiceSettingDefinition<string> Encoder { get; } = new(
        "AudioEncoder", "Audio encoder:", "Audio encoder:",
        "Choose the audio format for conversions and ripping. WAV and FLAC preserve audio quality. MP3 works with most players.",
        SettingCategory.Audio, Array.AsReadOnly<string>(["OGG Vorbis", "MP3", "WMA 2", "WAV (PCM)", "FLAC", "AAC (M4A)"]));
    public ChoiceSettingDefinition<int> Bitrate { get; } = new(
        "AudioBitrate", "Bitrate (kbps):", "Bitrates are remembered per encoder. WAV and FLAC are lossless.",
        "MP3, OGG, WMA and AAC each remember their own bitrate. For older karaoke players, pair MP3 with Deflate ZIP compression.",
        SettingCategory.Audio, Array.AsReadOnly<int>([64, 96, 128, 160, 192, 224, 256, 320]));
    public SettingDefinition ExperimentalDiscRipping { get; } = new(
        "ExperimentalDiscRipping", "Enable experimental CD+G ripping", "Experimental; requires a compatible drive.",
        "Show CD+G ripping and its settings. Requires cdrdao and a compatible disc drive.",
        SettingCategory.DiscRipping);
    public SettingDefinition CdrdaoExecutablePath { get; } = new(
        "CdrdaoExecutablePath", "cdrdao executable (optional)", "Leave blank for automatic discovery.",
        "Select the cdrdao program file to override automatic discovery. Leave blank to use the bundled copy or a system installation.",
        SettingCategory.DiscRipping);
    public ChoiceSettingDefinition<string> VideoContainer { get; } = new(
        "VideoContainer", "Output format", "Keeps the original files and fits graphics without stretching.",
        "MP4 and MKV use H.264 video with AAC audio when available. AVI uses MPEG-4 video with MP3 audio. Original karaoke files are kept.",
        SettingCategory.Video, Array.AsReadOnly<string>(["MP4 (default)", "MKV", "AVI"]));
    public ChoiceSettingDefinition<string> VideoResolution { get; } = new(
        "VideoResolution", "Resolution", "Resolution",
        "Set the video's width and height. The karaoke graphics keep their original proportions.",
        SettingCategory.Video, Array.AsReadOnly<string>(["640 × 480", "1280 × 720 (default)", "1920 × 1080", "2560 × 1440", "3840 × 2160"]));
    public ChoiceSettingDefinition<string> VideoGraphicsSmoothing { get; } = new(
        "VideoGraphicsSmoothing", "Graphics smoothing", "Changes how pixel edges look when enlarged.",
        "None keeps crisp pixels. Bilinear softens edges, Bicubic smooths more gradually, and Lanczos gives sharper edges that can show halos.",
        SettingCategory.Video, Array.AsReadOnly<string>(["None (default)", "Bilinear", "Bicubic", "Lanczos"]));
    public ChoiceSettingDefinition<int> VideoFrameRate { get; } = new(
        "VideoFrameRate", "Frames per second", "Frames per second",
        "Higher rates create more video frames. Audio and lyrics keep their original timing.",
        SettingCategory.Video, Array.AsReadOnly<int>([25, 30, 60]));
    public ChoiceSettingDefinition<int> VideoBitrateKbps { get; } = new(
        "VideoBitrateKbps", "Video bitrate (kbps)", "Video bitrate (kbps)",
        "Higher bitrates generally make larger video files. The original graphics set the level of detail.",
        SettingCategory.Video, Array.AsReadOnly<int>([1000, 2000, 4000, 6000]));
    public SettingDefinition VideoAudioMatchSourceBitrate { get; } = new(
        "VideoAudioMatchSourceBitrate", "Match compressed source audio bitrate when possible", "Uses the fallback when the source bitrate is unavailable.",
        "Use the source's average bitrate up to 320 kbps. Lossless audio and files with missing bitrate information use the fallback.",
        SettingCategory.Video);
    public ChoiceSettingDefinition<int> VideoAudioBitrateKbps { get; } = new(
        "VideoAudioBitrateKbps", "Fallback audio bitrate (kbps)", "Fallback audio bitrate (kbps)",
        "Use this bitrate when source matching is off or the source has no usable bitrate.",
        SettingCategory.Video, Array.AsReadOnly<int>([128, 192, 256, 320]));
    public HelpDefinition OnlineRenaming { get; } = new(
        "Online renaming is unavailable.", "These saved preferences are for older versions.");
    public HelpDefinition ExplorerOverview { get; } = new(
        "Open karaoke files and folders from their right-click menu.", "Add a command to open selected files or folders in Karaoke Toolkit for your Windows account. You choose when to start processing.");
    public HelpDefinition ExplorerLocation { get; } = new(
        "On Windows 11, look under Show more options.", "If you move the app, enable the command again to update its path.");
    public HelpDefinition EnableExplorer { get; } = new(
        "Enable / update", "Add or update the Explorer command for your Windows account.");
    public HelpDefinition RemoveExplorer { get; } = new("Remove", "Remove the Windows Explorer command for your account. Applies immediately.");
    public HelpDefinition ExplorerImmediate { get; } = new(
        "Applies immediately for your Windows account.", "Changes take effect immediately and work with a standard Windows account.");
    public HelpDefinition BrowseCdrdao { get; } = new("Browse…", "Choose a local cdrdao executable file. Use Apply or OK to save the selected path.");
    public HelpDefinition RipSharedSettings { get; } = new(
        "Uses General, Encoding and ZIP Compression settings.", "General controls CUE/TXT and existing files. Encoding and ZIP Compression control track output.");
    public HelpDefinition ResetDefaults { get; } = new(
        "Reset to Defaults…", "Restore defaults after confirmation. Select Apply or OK to save them.");
    public HelpDefinition Save { get; } = new("OK", "Save pending settings and close this window.");
    public HelpDefinition Cancel { get; } = new("Cancel", "Close without saving pending changes. Settings already saved with Apply remain in effect.");
    public HelpDefinition Apply { get; } = new("Apply", "Save pending settings and keep this window open.");
    public IReadOnlyList<SettingDefinition> All => Array.AsReadOnly<SettingDefinition>([
        Theme, RecurseFolders, IgnoreLeadingTheForFileOrganization, DeleteOriginals, ValidateBeforeConversion, AutoCreateCueText, Overwrite, AutoRenameZip, RenameBins, NamingFormat, Algorithm, CompressionLevel, Encoder, Bitrate, ExperimentalDiscRipping, CdrdaoExecutablePath, VideoContainer, VideoResolution, VideoGraphicsSmoothing, VideoFrameRate, VideoBitrateKbps, VideoAudioMatchSourceBitrate, VideoAudioBitrateKbps
    ]);
}
