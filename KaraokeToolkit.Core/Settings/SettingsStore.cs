using KaraokeToolkit.Core.Video;
using KaraokeToolkit.Core.Audio;
using KaraokeToolkit.Core.Archives;
using KaraokeToolkit.Core.Conversion;
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
    /// <remarks>Invalid XML or conversions propagate to the caller; the existing file is not repaired implicitly.</remarks>
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
            object converted = property.PropertyType == typeof(bool) ? Conversions.ToBoolean(value)
                : property.PropertyType == typeof(int) ? Conversions.ToInteger(value)
                : property.PropertyType.IsEnum ? Enum.ToObject(property.PropertyType, Conversions.ToInteger(value)) : value;
            property.SetValue(options, converted);
        }
        string? compression = doc.Root?.Element("KaraokeToolkit")?.Element("ZipCompression")?.Value;
        if (compression != null)
        {
            options = options with { ZipCompression = ZipCompressionMigration.FromSaved(compression) };
        }
        else
        {
            string? legacy = section?.Elements("entry").FirstOrDefault(e => (string?)e.Attribute("name") == "ZipCompressionLevel")?.Value;
            if (!string.IsNullOrEmpty(legacy)) options = options with { ZipCompression = ZipCompressionMigration.FromLegacy(Conversions.ToInteger(legacy)) };
        }
        string? algorithm = doc.Root?.Element("KaraokeToolkit")?.Element("ZipAlgorithm")?.Value;
        if (algorithm != null)
        {
            if (!Enum.TryParse<ZipAlgorithm>(algorithm, out var parsed) || !Enum.IsDefined(parsed))
                throw new InvalidDataException("Invalid ZIP algorithm.");
            options = options with { ZipAlgorithm = parsed };
        }
        string? experimental = doc.Root?.Element("KaraokeToolkit")?.Element("ExperimentalDiscRipping")?.Value;
        if (experimental != null)
        {
            if (!bool.TryParse(experimental, out bool enabled)) throw new InvalidDataException("Invalid experimental ripping preference.");
            options = options with { ExperimentalDiscRipping = enabled };
        }
        var video = doc.Root?.Element("KaraokeToolkit");
        options = options with { CdrdaoExecutablePath = video?.Element("CdrdaoExecutablePath")?.Value ?? "" };
        if (video?.Element("VideoContainer")?.Value is { } container)
        {
            if (!Enum.TryParse<VideoContainer>(container, out var parsed) || !Enum.IsDefined(parsed))
                throw new InvalidDataException("Invalid video container.");
            options = options with { VideoContainer = parsed };
        }
        if (video?.Element("VideoResolution")?.Value is { } resolution)
        {
            if (!Enum.TryParse<VideoResolution>(resolution, out var parsed) || !Enum.IsDefined(parsed))
                throw new InvalidDataException("Invalid video resolution.");
            options = options with { VideoResolution = parsed };
        }
        if (video?.Element("VideoGraphicsSmoothing")?.Value is { } smoothing)
        {
            if (!Enum.TryParse<VideoGraphicsSmoothing>(smoothing, out var parsed) || !Enum.IsDefined(parsed))
                throw new InvalidDataException("Invalid video graphics smoothing.");
            options = options with { VideoGraphicsSmoothing = parsed };
        }
        int ReadVideoInt(string name, int fallback)
        {
            string? value = video?.Element(name)?.Value;
            if (value == null) return fallback;
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                throw new InvalidDataException("Invalid " + name + " in settings.");
            return parsed;
        }
        options = options with
        {
            VideoFrameRate = ReadVideoInt("VideoFrameRate", options.VideoFrameRate),
            VideoBitrateKbps = ReadVideoInt("VideoBitrateKbps", options.VideoBitrateKbps),
            VideoAudioBitrateKbps = ReadVideoInt("VideoAudioBitrateKbps", options.VideoAudioBitrateKbps)
        };
        if (video?.Element("VideoAudioMatchSourceBitrate")?.Value is { } matchBitrate)
        {
            if (!bool.TryParse(matchBitrate, out bool parsed)) throw new InvalidDataException("Invalid video audio bitrate mode.");
            options = options with { VideoAudioMatchSourceBitrate = parsed };
        }
        VideoOptions.Validate(options);
        return options;
    }

    /// <summary>Updates known entries while retaining unknown XML entries and sections.</summary>
    /// <remarks>Serializes writers, flushes a same-directory temporary file, and atomically replaces the old XML.
    /// An optional extension updates additional XML entries within the same transaction. If it throws,
    /// the existing file remains unchanged. Core does not interpret these caller-owned entries.</remarks>
    public void Save(ConversionOptions options, OverwritePolicy? overwritePolicy = null,
        bool? validateBeforeConversion = null, bool? ignoreLeadingTheForFileOrganization = null,
        AudioBitratePreferences? audioBitrates = null, Action<XElement>? updateAdditionalPreferences = null)
    {
        ArgumentNullException.ThrowIfNull(options);
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
    /// <summary>Modern-only preference; absent legacy files default to asking before overwrites.</summary>
    public OverwritePolicy LoadOverwritePolicy()
    {
        if (!File.Exists(filename))
            return OverwritePolicy.Ask;
        string? value = XDocument.Load(filename).Root?.Element("KaraokeToolkit")?.Element("OverwritePolicy")?.Value;
        if (value == null)
            return OverwritePolicy.Ask;
        if (!Enum.TryParse<OverwritePolicy>(value, out var policy) || !Enum.IsDefined(policy))
            throw new InvalidDataException("Invalid overwrite policy in settings.");
        return policy;
    }
    public bool LoadValidateBeforeConversion()
    {
        if (!File.Exists(filename))
            return false;
        string? value = XDocument.Load(filename).Root?.Element("KaraokeToolkit")?.Element("ValidateBeforeConversion")?.Value;
        if (value == null)
            return false;
        if (!bool.TryParse(value, out bool enabled))
            throw new InvalidDataException("Invalid conversion validation preference.");
        return enabled;
    }
    public bool LoadIgnoreLeadingTheForFileOrganization()
    {
        if (!File.Exists(filename)) return false;
        string? value = XDocument.Load(filename).Root?.Element("KaraokeToolkit")?.Element("IgnoreLeadingTheForFileOrganization")?.Value;
        if (value == null) return false;
        if (!bool.TryParse(value, out bool enabled))
            throw new InvalidDataException("Invalid leading-The file organization preference.");
        return enabled;
    }
    public AudioBitratePreferences LoadAudioBitrates(ConversionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var fallback = new AudioBitratePreferences().With(options.AudioEncoder, options.AudioBitrate);
        if (!File.Exists(filename)) return fallback;
        var modern = XDocument.Load(filename).Root?.Element("KaraokeToolkit");
        int Read(string name, int defaultValue)
        {
            string? value = modern?.Element(name)?.Value;
            if (value == null) return defaultValue;
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                throw new InvalidDataException("Invalid " + name + " in settings.");
            return parsed;
        }
        var result = new AudioBitratePreferences(Read("Mp3Bitrate", fallback.Mp3), Read("OggBitrate", fallback.Ogg),
            Read("WmaBitrate", fallback.Wma), Read("AacBitrate", fallback.Aac));
        result.Validate();
        return result;
    }
}
