using System.Globalization;
using System.Xml.Linq;
using KaraokeToolkit.Core.Archives;
using KaraokeToolkit.Core.Audio;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Execution;
using KaraokeToolkit.Core.Recovery;
using KaraokeToolkit.Core.Tagging;

namespace KaraokeToolkit.Core.Settings;

internal static class SettingsXml
{
    internal static XDocument Create() => new(new XDeclaration("1.0", "utf-8", null),
        new XElement("KaraokeToolkit", new XAttribute("schemaVersion", "1"),
            new XElement("Processing"), new XElement("Desktop")));

    internal static XElement Validate(XDocument document)
    {
        var root = document.Root;
        if (root?.Name != "KaraokeToolkit" || (string?)root.Attribute("schemaVersion") != "1")
            throw new InvalidDataException("Unsupported settings profile format. Check that the toolkit supports this schema version.");

        if (root.Elements("Processing").Skip(1).Any() || root.Elements("Desktop").Skip(1).Any() ||
            root.Elements("AppliedSettingsMigrations").Skip(1).Any())
            throw new InvalidDataException("The settings profile contains duplicate sections.");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var setting in root.Elements("Processing").Elements("Setting"))
        {
            string? id = (string?)setting.Attribute("id");
            if (string.IsNullOrWhiteSpace(id) || !ids.Add(id))
                throw new InvalidDataException("Each processing setting needs a unique ID.");
        }
        return root;
    }

    internal static XElement Section(XElement root, XName name)
    {
        if (root.Element(name) is { } section) return section;
        section = new XElement(name);
        root.Add(section);
        return section;
    }

    internal static void Write(XElement root, string id, object value)
    {
        var processing = Section(root, "Processing");
        var setting = Find(root, id);
        if (setting == null)
        {
            setting = new XElement("Setting", new XAttribute("id", id));
            processing.Add(setting);
        }
        setting.SetValue(value);
    }

    internal static void Sort(XElement root)
    {
        if (root.Element("Processing") is { } processing)
        {
            var settings = processing.Elements("Setting").OrderBy(node => (string?)node.Attribute("id"), StringComparer.Ordinal).ToArray();
            foreach (var setting in settings) setting.Remove();
            processing.AddFirst(settings);
        }
        foreach (string name in new[] { "AppliedSettingsMigrations", "Desktop", "Processing" })
        {
            if (root.Element(name) is not { } section) continue;
            section.Remove();
            root.AddFirst(section);
        }
    }

    internal static SharedSettingsSnapshot Read(XElement root, bool persistRenameBins = true)
    {
        var defaults = new ConversionOptions();
        var encoder = Read(root, "audio.encoder", defaults.AudioEncoder);
        int ReadBitrate(AudioEncoder codec) => ReadBitrateValue(root, BitrateId(codec));
        var audioBitrates = new AudioBitratePreferences(
            SupportedBitrate(AudioEncoder.MP3), SupportedBitrate(AudioEncoder.OGG),
            SupportedBitrate(AudioEncoder.WMA), SupportedBitrate(AudioEncoder.AAC));
        var options = defaults with
        {
            AudioEncoder = encoder,
            AudioBitrate = ReadBitrate(encoder),
            AutoCreateCueText = Read(root, "metadata.create-cue-text", defaults.AutoCreateCueText),
            TrackListingName = Read(root, "metadata.track-listing-name", defaults.TrackListingName),
            CueSheetName = Read(root, "metadata.cue-sheet-name", defaults.CueSheetName),
            AutoRenameZip = Read(root, "zip.auto-rename", defaults.AutoRenameZip),
            DeleteOldFiles = Read(root, "files.delete-originals", defaults.DeleteOldFiles),
            LastDiscNumber = Read(root, "disc.last-number", defaults.LastDiscNumber),
            LastOutputFolder = Read(root, "files.last-output-folder", defaults.LastOutputFolder),
            NamingFormat = Read(root, "files.naming-format", defaults.NamingFormat),
            RecurseFolders = Read(root, "files.recurse", defaults.RecurseFolders),
            RenameBins = !persistRenameBins || Read(root, "files.rename-bins", defaults.RenameBins),
            ExperimentalDiscRipping = Read(root, "disc.experimental", defaults.ExperimentalDiscRipping),
            CdrdaoExecutablePath = Read(root, "disc.cdrdao-path", defaults.CdrdaoExecutablePath),
            ZipAlgorithm = Read(root, "zip.algorithm", defaults.ZipAlgorithm),
            ZipCompression = (ZipCompression)Read(root, "zip.level", (int)defaults.ZipCompression),
            VideoContainer = Read(root, "video.container", defaults.VideoContainer),
            VideoResolution = Read(root, "video.resolution", defaults.VideoResolution),
            VideoGraphicsSmoothing = Read(root, "video.graphics-smoothing", defaults.VideoGraphicsSmoothing),
            VideoFrameRate = Read(root, "video.frame-rate", defaults.VideoFrameRate),
            VideoBitrateKbps = Read(root, "video.bitrate", defaults.VideoBitrateKbps),
            VideoAudioMatchSourceBitrate = Read(root, "video.match-source-audio-bitrate", defaults.VideoAudioMatchSourceBitrate),
            VideoAudioBitrateKbps = Read(root, "video.audio-bitrate", defaults.VideoAudioBitrateKbps)
        };

        return new(options, audioBitrates, Read(root, "files.overwrite", OverwritePolicy.Ask),
            Read(root, "files.validate-before-conversion", false), Read(root, "files.ignore-leading-the", false),
            Read(root, "tags.filename-pattern", FilenameTagPattern.Default),
            Read(root, "recovery.verification", OperationCapabilities.DefaultFileWriteRecoveryVerification),
            Read(root, "recovery.batch-resume", true), Read(root, "recovery.check-on-startup", true),
            Read(root, "recovery.check-timing", OperationCapabilities.DefaultFileMoveCheckTiming),
            Read(root, "recovery.move-verification", OperationCapabilities.DefaultFileMoveRecoveryVerification));

        int SupportedBitrate(AudioEncoder codec)
        {
            int value = ReadBitrate(codec);
            return AudioEncoderService.SupportedBitrates.Contains(value) ? value : ConversionOptions.DefaultAudioBitrate;
        }
    }

    internal static void WriteOptions(XElement root, ConversionOptions options, bool persistRenameBins)
    {
        Write(root, "audio.encoder", options.AudioEncoder);
        Write(root, BitrateId(options.AudioEncoder), options.AudioBitrate);
        Write(root, "metadata.create-cue-text", options.AutoCreateCueText);
        Write(root, "metadata.track-listing-name", options.TrackListingName);
        Write(root, "metadata.cue-sheet-name", options.CueSheetName);
        Write(root, "zip.auto-rename", options.AutoRenameZip);
        Write(root, "files.delete-originals", options.DeleteOldFiles);
        Write(root, "disc.last-number", options.LastDiscNumber);
        Write(root, "files.last-output-folder", options.LastOutputFolder);
        Write(root, "files.naming-format", options.NamingFormat);
        Write(root, "files.recurse", options.RecurseFolders);
        if (persistRenameBins) Write(root, "files.rename-bins", options.RenameBins);
        Write(root, "disc.experimental", options.ExperimentalDiscRipping);
        Write(root, "disc.cdrdao-path", options.CdrdaoExecutablePath);
        Write(root, "zip.algorithm", options.ZipAlgorithm);
        Write(root, "zip.level", (int)options.ZipCompression);
        Write(root, "video.container", options.VideoContainer);
        Write(root, "video.resolution", options.VideoResolution);
        Write(root, "video.graphics-smoothing", options.VideoGraphicsSmoothing);
        Write(root, "video.frame-rate", options.VideoFrameRate);
        Write(root, "video.bitrate", options.VideoBitrateKbps);
        Write(root, "video.match-source-audio-bitrate", options.VideoAudioMatchSourceBitrate);
        Write(root, "video.audio-bitrate", options.VideoAudioBitrateKbps);
    }

    private static string BitrateId(AudioEncoder encoder) => encoder switch
    {
        AudioEncoder.MP3 => "audio.mp3-bitrate",
        AudioEncoder.OGG => "audio.ogg-bitrate",
        AudioEncoder.WMA => "audio.wma-bitrate",
        AudioEncoder.AAC => "audio.aac-bitrate",
        _ => "audio.bitrate"
    };

    private static int ReadBitrateValue(XElement root, string id) =>
        int.TryParse(Find(root, id)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) &&
        value is > 0 and <= short.MaxValue ? value : ConversionOptions.DefaultAudioBitrate;

    private static T Read<T>(XElement root, string id, T fallback)
    {
        if (Find(root, id) is not { } field) return fallback;
        try { return (T)SharedSettingCatalog.All[id].Parse(field.Value); }
        catch (ArgumentException) { return fallback; }
    }

    private static XElement? Find(XElement root, string id) =>
        root.Element("Processing")?.Elements("Setting").FirstOrDefault(field => (string?)field.Attribute("id") == id);
}
