using KaraokeToolkit.Core.Video;
using KaraokeToolkit.Core.Audio;
using KaraokeToolkit.Core.Archives;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.FileOperations;
using System.Globalization;
using System.Xml.Linq;
using Microsoft.VisualBasic.CompilerServices;

namespace KaraokeToolkit.Core.Settings;

/// <summary>Explicit-path store for the original Options XML schema. Does not touch installed app settings.</summary>
public sealed class SettingsStore(string path, bool persistRenameBins = false)
{
    private readonly string filename = Path.GetFullPath(path);
    // Legacy callers retain the original omission; the modern frontend opts into full persistence.
    private readonly System.Reflection.PropertyInfo[] Persisted = typeof(ConversionOptions)
        .GetProperties().Where(p => p.Name != nameof(ConversionOptions.CdrdaoExecutablePath) && p.Name != nameof(ConversionOptions.ExperimentalDiscRipping) && p.Name != nameof(ConversionOptions.ZipAlgorithm) && p.Name != nameof(ConversionOptions.ZipCompression) &&
            p.Name is not (nameof(ConversionOptions.VideoContainer) or nameof(ConversionOptions.VideoResolution) or nameof(ConversionOptions.VideoGraphicsSmoothing) or nameof(ConversionOptions.VideoFrameRate) or nameof(ConversionOptions.VideoBitrateKbps) or nameof(ConversionOptions.VideoAudioMatchSourceBitrate) or nameof(ConversionOptions.VideoAudioBitrateKbps)) &&
            (persistRenameBins || p.Name != nameof(ConversionOptions.RenameBins))).ToArray();

    /// <summary>Loads known options, using original defaults for absent entries.</summary>
    /// <remarks>Unsupported values use their setting's default. Invalid XML propagates to the caller.</remarks>
    public ConversionOptions Load()
    {
        var options = new ConversionOptions();
        if (!File.Exists(filename))
            return options;
        var doc = XDocument.Load(filename);
        var section = doc.Root?.Elements("section").FirstOrDefault(e => (string?)e.Attribute("name") == "Options");
        foreach (var property in Persisted)
        {
            string? value = section?.Elements("entry").FirstOrDefault(e => (string?)e.Attribute("name") == property.Name)?.Value;
            if (string.IsNullOrEmpty(value))
                continue; // Original GetValueAsString fallback.
            object converted;
            try
            {
                converted = property.PropertyType == typeof(bool) ? Conversions.ToBoolean(value)
                    : property.PropertyType == typeof(int) ? Conversions.ToInteger(value)
                    : property.PropertyType.IsEnum ? Enum.ToObject(property.PropertyType, Conversions.ToInteger(value)) : value;
                if (property.Name == nameof(ConversionOptions.TrackListingName)) MetadataFileNames.Resolve(value, "Example folder", ".txt");
                if (property.Name == nameof(ConversionOptions.CueSheetName)) MetadataFileNames.Resolve(value, "Example folder", ".cue");
            }
            catch (Exception error) when (error is InvalidCastException or FormatException or OverflowException or ArgumentException) { continue; }
            if (property.PropertyType.IsEnum && !Enum.IsDefined(property.PropertyType, converted)) continue;
            if (property.Name == nameof(ConversionOptions.AudioBitrate) && (int)converted is <= 0 or > short.MaxValue) continue;
            property.SetValue(options, converted);
        }
        string? compression = doc.Root?.Element("KaraokeToolkit")?.Element("ZipCompression")?.Value;
        try
        {
            if (compression != null)
                options = options with { ZipCompression = ZipCompressionMigration.FromSaved(compression) };
            else
            {
                string? legacy = section?.Elements("entry").FirstOrDefault(e => (string?)e.Attribute("name") == "ZipCompressionLevel")?.Value;
                if (!string.IsNullOrEmpty(legacy)) options = options with { ZipCompression = ZipCompressionMigration.FromLegacy(Conversions.ToInteger(legacy)) };
            }
        }
        catch (Exception error) when (error is InvalidDataException or ArgumentOutOfRangeException or InvalidCastException or FormatException or OverflowException) { }
        string? algorithm = doc.Root?.Element("KaraokeToolkit")?.Element("ZipAlgorithm")?.Value;
        if (Enum.TryParse<ZipAlgorithm>(algorithm, out var parsedAlgorithm) && Enum.IsDefined(parsedAlgorithm))
        {
            options = options with { ZipAlgorithm = parsedAlgorithm };
        }
        string? experimental = doc.Root?.Element("KaraokeToolkit")?.Element("ExperimentalDiscRipping")?.Value;
        if (bool.TryParse(experimental, out bool enabled))
        {
            options = options with { ExperimentalDiscRipping = enabled };
        }
        var video = doc.Root?.Element("KaraokeToolkit");
        options = options with { CdrdaoExecutablePath = video?.Element("CdrdaoExecutablePath")?.Value ?? "" };
        if (Enum.TryParse<VideoContainer>(video?.Element("VideoContainer")?.Value, out var container) && Enum.IsDefined(container))
        {
            options = options with { VideoContainer = container };
        }
        if (Enum.TryParse<VideoResolution>(video?.Element("VideoResolution")?.Value, out var resolution) && Enum.IsDefined(resolution))
        {
            options = options with { VideoResolution = resolution };
        }
        if (Enum.TryParse<VideoGraphicsSmoothing>(video?.Element("VideoGraphicsSmoothing")?.Value, out var smoothing) && Enum.IsDefined(smoothing))
        {
            options = options with { VideoGraphicsSmoothing = smoothing };
        }
        int ReadVideoInt(string name, int fallback, Func<int, bool> supported)
        {
            string? value = video?.Element(name)?.Value;
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) && supported(parsed)
                ? parsed : fallback;
        }
        options = options with
        {
            VideoFrameRate = ReadVideoInt("VideoFrameRate", options.VideoFrameRate, VideoOptions.SupportsFrameRate),
            VideoBitrateKbps = ReadVideoInt("VideoBitrateKbps", options.VideoBitrateKbps, VideoOptions.SupportsVideoBitrate),
            VideoAudioBitrateKbps = ReadVideoInt("VideoAudioBitrateKbps", options.VideoAudioBitrateKbps, VideoOptions.SupportsAudioBitrate)
        };
        if (bool.TryParse(video?.Element("VideoAudioMatchSourceBitrate")?.Value, out bool matchBitrate))
        {
            options = options with { VideoAudioMatchSourceBitrate = matchBitrate };
        }
        VideoOptions.Validate(options);
        return options;
    }

    /// <summary>Updates known entries and optionally removes unknown keys from the two settings sections.</summary>
    /// <remarks>Serializes writers, flushes a same-directory temporary file, and atomically replaces the old XML.
    /// An optional extension updates additional XML entries within the same transaction. If it throws,
    /// the existing file remains unchanged. Supply the caller's known keys to opt into cleanup.</remarks>
    public void Save(ConversionOptions options, OverwritePolicy? overwritePolicy = null,
        bool? validateBeforeConversion = null, bool? ignoreLeadingTheForFileOrganization = null,
        AudioBitratePreferences? audioBitrates = null, Action<XElement>? updateAdditionalPreferences = null,
        IReadOnlyCollection<string>? knownAdditionalKeysForCleanup = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        MetadataFileNames.Resolve(options.TrackListingName, "Example folder", ".txt");
        MetadataFileNames.Resolve(options.CueSheetName, "Example folder", ".cue");
        if (!Enum.IsDefined(options.ZipAlgorithm)) throw new ArgumentOutOfRangeException(nameof(options.ZipAlgorithm));
        if (!Enum.IsDefined(options.ZipCompression)) throw new ArgumentOutOfRangeException(nameof(options.ZipCompression));
        VideoOptions.Validate(options);
        if (overwritePolicy.HasValue && !Enum.IsDefined(overwritePolicy.Value))
            throw new ArgumentOutOfRangeException(nameof(overwritePolicy));
        audioBitrates?.Validate();
        string key = OperatingSystem.IsWindows() ? filename.ToUpperInvariant() : filename;
        using var mutex = new Mutex(false, "KaraokeToolkit-Settings-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key))));
        bool acquired = false;
        try
        {
            try
            {
                acquired = mutex.WaitOne(TimeSpan.FromSeconds(5));
            }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired)
                throw new IOException("Settings are being saved by another process.");
            var doc = File.Exists(filename) ? XDocument.Load(filename) : new XDocument(new XElement("profile"));
            var root = doc.Root ?? throw new InvalidDataException("Settings XML has no root.");
            var section = root.Elements("section").FirstOrDefault(e => (string?)e.Attribute("name") == "Options");
            if (section == null)
            {
                section = new XElement("section", new XAttribute("name", "Options"));
                root.Add(section);
            }
            foreach (var property in Persisted)
            {
                var entry = section.Elements("entry").FirstOrDefault(e => (string?)e.Attribute("name") == property.Name);
                if (entry == null)
                {
                    entry = new XElement("entry", new XAttribute("name", property.Name));
                    section.Add(entry);
                }
                object value = property.GetValue(options)!;
                entry.Value = property.PropertyType.IsEnum ? Convert.ToInt32(value).ToString(CultureInfo.CurrentCulture) : Convert.ToString(value, CultureInfo.CurrentCulture)!;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(filename)!);
            // Keep a representative level for readers using the old settings schema.
            var legacyCompression = section.Elements("entry").FirstOrDefault(e => (string?)e.Attribute("name") == "ZipCompressionLevel");
            if (legacyCompression == null) { legacyCompression = new XElement("entry", new XAttribute("name", "ZipCompressionLevel")); section.Add(legacyCompression); }
            legacyCompression.Value = ZipCompressionMigration.ToLegacy(options.ZipCompression).ToString(CultureInfo.InvariantCulture);
            {
                var modern = root.Element("KaraokeToolkit");
                if (modern == null)
                {
                    modern = new XElement("KaraokeToolkit");
                    root.Add(modern);
                }
                modern.SetElementValue("ExperimentalDiscRipping", options.ExperimentalDiscRipping);
                modern.SetElementValue("CdrdaoExecutablePath", options.CdrdaoExecutablePath);
                modern.SetElementValue("ZipAlgorithm", options.ZipAlgorithm.ToString());
                modern.SetElementValue("ZipCompression", ZipCompressionMigration.ToLegacy(options.ZipCompression).ToString(CultureInfo.InvariantCulture));
                modern.SetElementValue("VideoContainer", options.VideoContainer);
                modern.SetElementValue("VideoResolution", options.VideoResolution);
                modern.SetElementValue("VideoGraphicsSmoothing", options.VideoGraphicsSmoothing);
                modern.SetElementValue("VideoFrameRate", options.VideoFrameRate);
                modern.SetElementValue("VideoBitrateKbps", options.VideoBitrateKbps);
                modern.SetElementValue("VideoAudioMatchSourceBitrate", options.VideoAudioMatchSourceBitrate);
                modern.SetElementValue("VideoAudioBitrateKbps", options.VideoAudioBitrateKbps);
                if (overwritePolicy.HasValue)
                    modern.SetElementValue("OverwritePolicy", overwritePolicy.Value);
                if (validateBeforeConversion.HasValue)
                    modern.SetElementValue("ValidateBeforeConversion", validateBeforeConversion.Value);
                if (ignoreLeadingTheForFileOrganization.HasValue)
                    modern.SetElementValue("IgnoreLeadingTheForFileOrganization", ignoreLeadingTheForFileOrganization.Value);
                if (audioBitrates != null)
                {
                    modern.SetElementValue("Mp3Bitrate", audioBitrates.Mp3);
                    modern.SetElementValue("OggBitrate", audioBitrates.Ogg);
                    modern.SetElementValue("WmaBitrate", audioBitrates.Wma);
                    modern.SetElementValue("AacBitrate", audioBitrates.Aac);
                }
                updateAdditionalPreferences?.Invoke(modern);
                if (knownAdditionalKeysForCleanup != null)
                    RemoveUnknownKeys(section, modern, knownAdditionalKeysForCleanup);
            }
            string temporary = filename + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    doc.Save(file);
                    file.Flush(flushToDisk: true);
                }
                if (File.Exists(filename))
                    File.Replace(temporary, filename, filename + ".bak");
                else
                    File.Move(temporary, filename);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        finally { if (acquired) mutex.ReleaseMutex(); }
    }

    private void RemoveUnknownKeys(XElement section, XElement modern, IReadOnlyCollection<string> additionalKeys)
    {
        var legacyKeys = Persisted.Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
        legacyKeys.Add(nameof(ConversionOptions.RenameBins));
        legacyKeys.Add("ZipCompressionLevel");
        section.Elements("entry").Where(entry => !legacyKeys.Contains((string?)entry.Attribute("name") ?? "")).Remove();

        var modernKeys = new HashSet<XName>
        {
            "ExperimentalDiscRipping", "CdrdaoExecutablePath", "ZipAlgorithm", "ZipCompression",
            "VideoContainer", "VideoResolution", "VideoGraphicsSmoothing", "VideoFrameRate",
            "VideoBitrateKbps", "VideoAudioMatchSourceBitrate", "VideoAudioBitrateKbps", "OverwritePolicy",
            "ValidateBeforeConversion", "IgnoreLeadingTheForFileOrganization",
            "Mp3Bitrate", "OggBitrate", "WmaBitrate", "AacBitrate"
        };
        modernKeys.UnionWith(additionalKeys.Select(key => XName.Get(key)));
        modern.Elements().Where(entry => !modernKeys.Contains(entry.Name)).Remove();
    }
    /// <summary>Modern-only preference; absent legacy files default to asking before overwrites.</summary>
    public OverwritePolicy LoadOverwritePolicy()
    {
        if (!File.Exists(filename))
            return OverwritePolicy.Ask;
        string? value = XDocument.Load(filename).Root?.Element("KaraokeToolkit")?.Element("OverwritePolicy")?.Value;
        return Enum.TryParse<OverwritePolicy>(value, out var policy) && Enum.IsDefined(policy)
            ? policy : OverwritePolicy.Ask;
    }
    public bool LoadValidateBeforeConversion()
    {
        if (!File.Exists(filename))
            return false;
        string? value = XDocument.Load(filename).Root?.Element("KaraokeToolkit")?.Element("ValidateBeforeConversion")?.Value;
        return bool.TryParse(value, out bool enabled) && enabled;
    }
    public bool LoadIgnoreLeadingTheForFileOrganization()
    {
        if (!File.Exists(filename)) return false;
        string? value = XDocument.Load(filename).Root?.Element("KaraokeToolkit")?.Element("IgnoreLeadingTheForFileOrganization")?.Value;
        return bool.TryParse(value, out bool enabled) && enabled;
    }
    public AudioBitratePreferences LoadAudioBitrates(ConversionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var fallback = new AudioBitratePreferences();
        if (AudioEncoderService.SupportedBitrates.Contains(options.AudioBitrate))
            fallback = fallback.With(options.AudioEncoder, options.AudioBitrate);
        if (!File.Exists(filename)) return fallback;
        var modern = XDocument.Load(filename).Root?.Element("KaraokeToolkit");
        int Read(string name, int defaultValue)
        {
            string? value = modern?.Element(name)?.Value;
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) &&
                AudioEncoderService.SupportedBitrates.Contains(parsed) ? parsed : defaultValue;
        }
        var result = new AudioBitratePreferences(Read("Mp3Bitrate", fallback.Mp3), Read("OggBitrate", fallback.Ogg),
            Read("WmaBitrate", fallback.Wma), Read("AacBitrate", fallback.Aac));
        result.Validate();
        return result;
    }
}
