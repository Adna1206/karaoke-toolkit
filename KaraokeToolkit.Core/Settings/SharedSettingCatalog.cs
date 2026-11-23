using System.Globalization;
using KaraokeToolkit.Core.Archives;
using KaraokeToolkit.Core.Audio;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Execution;
using KaraokeToolkit.Core.FileOperations;
using KaraokeToolkit.Core.Recovery;
using KaraokeToolkit.Core.Tagging;
using KaraokeToolkit.Core.Video;

namespace KaraokeToolkit.Core.Settings;

internal static class SharedSettingCatalog
{
    private const SettingAccess Shared = SettingAccess.ReadWrite;
    private const SettingAccess DesktopOnly = SettingAccess.Hidden;
    private static readonly ConversionOptions Defaults = new();
    private static readonly AudioBitratePreferences Bitrates = new();

    internal static readonly IReadOnlyDictionary<string, SettingDefinition> All = new SettingDefinition[]
    {
        Define("recovery.verification", OperationCapabilities.DefaultFileMoveRecoveryVerification, Shared, Shared, s => s.RecoveryVerification),
        Define("recovery.check-timing", OperationCapabilities.DefaultFileMoveCheckTiming, Shared, Shared, s => s.CheckTiming),
        Define("recovery.batch-resume", true, Shared, Shared, s => s.SaveResumableRun),
        Define("recovery.check-on-startup", true, Shared, Shared, s => s.CheckForInterruptedRunsOnStartup),
        Define("files.overwrite", OverwritePolicy.Ask, Shared, Shared, s => s.Overwrite),
        Define("files.validate-before-conversion", false, Shared, Shared, s => s.ValidateBeforeConversion),
        Define("files.ignore-leading-the", false, Shared, Shared, s => s.IgnoreLeadingThe),
        Define("files.recurse", Defaults.RecurseFolders, Shared, Shared, s => s.Options.RecurseFolders),
        Define("files.delete-originals", Defaults.DeleteOldFiles, DesktopOnly, DesktopOnly, s => s.Options.DeleteOldFiles),
        Define("files.rename-bins", Defaults.RenameBins, Shared, Shared, s => s.Options.RenameBins),
        Define("files.last-output-folder", Defaults.LastOutputFolder, DesktopOnly, DesktopOnly, s => s.Options.LastOutputFolder),
        Define("files.naming-format", Defaults.NamingFormat, DesktopOnly, DesktopOnly, s => s.Options.NamingFormat),
        Define("disc.last-number", Defaults.LastDiscNumber, DesktopOnly, DesktopOnly, s => s.Options.LastDiscNumber),
        Define("disc.experimental", Defaults.ExperimentalDiscRipping, DesktopOnly, DesktopOnly, s => s.Options.ExperimentalDiscRipping),
        Define("disc.cdrdao-path", Defaults.CdrdaoExecutablePath, DesktopOnly, DesktopOnly, s => s.Options.CdrdaoExecutablePath),
        Define("metadata.create-cue-text", Defaults.AutoCreateCueText, Shared, Shared, s => s.Options.AutoCreateCueText),
        Define("metadata.track-listing-name", Defaults.TrackListingName, Shared, Shared, s => s.Options.TrackListingName,
            value => { MetadataFileNames.Resolve(value, "Example folder", ".txt"); return value; }),
        Define("metadata.cue-sheet-name", Defaults.CueSheetName, Shared, Shared, s => s.Options.CueSheetName,
            value => { MetadataFileNames.Resolve(value, "Example folder", ".cue"); return value; }),
        Define("tags.filename-pattern", FilenameTagPattern.Default, Shared, Shared, s => s.TaggingPattern,
            value => { _ = new FilenameTagPattern(value); return value; }),
        Define("audio.encoder", Defaults.AudioEncoder, Shared, Shared, s => s.Options.AudioEncoder),
        Define("audio.mp3-bitrate", Bitrates.Mp3, Shared, Shared, s => s.AudioBitrates.Mp3, CodecBitrate),
        Define("audio.ogg-bitrate", Bitrates.Ogg, Shared, Shared, s => s.AudioBitrates.Ogg, CodecBitrate),
        Define("audio.wma-bitrate", Bitrates.Wma, Shared, Shared, s => s.AudioBitrates.Wma, CodecBitrate),
        Define("audio.aac-bitrate", Bitrates.Aac, Shared, Shared, s => s.AudioBitrates.Aac, CodecBitrate),
        Define("zip.algorithm", Defaults.ZipAlgorithm, Shared, Shared, s => s.Options.ZipAlgorithm),
        Define("zip.auto-rename", Defaults.AutoRenameZip, DesktopOnly, DesktopOnly, s => s.Options.AutoRenameZip),
        Define("zip.level", (int)Defaults.ZipCompression, Shared, Shared, s => (int)s.Options.ZipCompression,
            text => CheckedInt(text, level => Enum.IsDefined((ZipCompression)level)),
            Enumerable.Range(0, 10).Select(n => n.ToString(CultureInfo.InvariantCulture)).ToArray()),
        Define("video.container", Defaults.VideoContainer, Shared, Shared, s => s.Options.VideoContainer),
        Define("video.resolution", Defaults.VideoResolution, Shared, Shared, s => s.Options.VideoResolution),
        Define("video.graphics-smoothing", Defaults.VideoGraphicsSmoothing, Shared, Shared, s => s.Options.VideoGraphicsSmoothing),
        Define("video.frame-rate", Defaults.VideoFrameRate, Shared, Shared, s => s.Options.VideoFrameRate, text => CheckedInt(text, VideoOptions.SupportsFrameRate)),
        Define("video.bitrate", Defaults.VideoBitrateKbps, Shared, Shared, s => s.Options.VideoBitrateKbps, text => CheckedInt(text, VideoOptions.SupportsVideoBitrate)),
        Define("video.match-source-audio-bitrate", Defaults.VideoAudioMatchSourceBitrate, Shared, Shared, s => s.Options.VideoAudioMatchSourceBitrate),
        Define("video.audio-bitrate", Defaults.VideoAudioBitrateKbps, Shared, Shared, s => s.Options.VideoAudioBitrateKbps, text => CheckedInt(text, VideoOptions.SupportsAudioBitrate))
    }.ToDictionary(setting => setting.Id, StringComparer.Ordinal);

    private static SettingDefinition<T> Define<T>(string id, T defaultValue, SettingAccess cli, SettingAccess mcp,
        Func<SharedSettingsSnapshot, T> read, Func<string, T>? parse = null, IReadOnlyList<string>? choices = null)
        where T : notnull =>
        new(id, defaultValue, cli, mcp, read, parse ?? Parse<T>, choices ?? Choices<T>());

    private static string[] Choices<T>() => typeof(T).IsEnum ? Enum.GetNames(typeof(T)) :
        typeof(T) == typeof(bool) ? ["true", "false"] : [];

    private static T Parse<T>(string text)
    {
        if (typeof(T) == typeof(string)) return (T)(object)text;
        if (typeof(T) == typeof(bool) && bool.TryParse(text, out bool flag)) return (T)(object)flag;
        if (typeof(T) == typeof(int) && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number)) return (T)(object)number;
        if (typeof(T).IsEnum && Enum.GetNames(typeof(T)).FirstOrDefault(name => name.Equals(text, StringComparison.OrdinalIgnoreCase)) is { } match)
            return (T)Enum.Parse(typeof(T), match);
        throw new ArgumentException($"Expected a {typeof(T).Name} value.");
    }

    private static int CodecBitrate(string text) => CheckedInt(text, value => AudioEncoderService.SupportedBitrates.Contains(value));

    private static int CheckedInt(string text, Func<int, bool> supported)
    {
        int number = Parse<int>(text);
        return supported(number) ? number : throw new ArgumentException("Choose a supported value.");
    }
}
