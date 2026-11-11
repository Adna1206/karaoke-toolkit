using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Xml.Linq;
using KaraokeToolkit.App.Metadata;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Settings;
using KaraokeToolkit.Core.Tagging;

namespace KaraokeToolkit.App.Features.Settings;

/// <summary>Composes processing settings with desktop preferences in the existing settings file.</summary>
/// <remarks>UI entries keep their original XML names so existing installations retain their preferences.
/// All entries are published together using Core's atomic settings transaction.</remarks>
public sealed class ApplicationSettingsStore(string path, bool persistRenameBins = false)
{
    private readonly string filename = Path.GetFullPath(path);
    private readonly SettingsStore processing = new(path, persistRenameBins);
    private static readonly string[] PreferenceKeys =
    [
        "Theme", "Backdrop", "TaggingPatterns", "LastTaggingPattern", "OperationSections",
        "FavouriteOperations", "SettingsWindowWidth", "SettingsWindowHeight"
    ];
    private static readonly HashSet<XName> SectionKeys = Enum.GetNames<OperationSection>().Select(name => XName.Get(name)).ToHashSet();

    public ConversionOptions Load() => processing.Load();
    public OverwritePolicy LoadOverwritePolicy() => processing.LoadOverwritePolicy();
    public bool LoadValidateBeforeConversion() => processing.LoadValidateBeforeConversion();
    public bool LoadIgnoreLeadingTheForFileOrganization() => processing.LoadIgnoreLeadingTheForFileOrganization();
    public AudioBitratePreferences LoadAudioBitrates(ConversionOptions options) => processing.LoadAudioBitrates(options);

    public void Save(ConversionOptions options, OverwritePolicy? overwritePolicy = null, ApplicationTheme? theme = null,
        bool? validateBeforeConversion = null, bool? ignoreLeadingTheForFileOrganization = null,
        AudioBitratePreferences? audioBitrates = null, IReadOnlyDictionary<string, bool>? operationSections = null,
        IReadOnlyList<string>? favouriteOperations = null, SettingsWindowSize? settingsWindowSize = null,
        ApplicationBackdrop? backdrop = null, IReadOnlyList<string>? taggingPatterns = null, string? lastTaggingPattern = null)
    {
        if (settingsWindowSize is { IsValid: false }) throw new ArgumentOutOfRangeException(nameof(settingsWindowSize));
        if (theme.HasValue && !Enum.IsDefined(theme.Value)) throw new ArgumentOutOfRangeException(nameof(theme));
        if (backdrop.HasValue && !Enum.IsDefined(backdrop.Value)) throw new ArgumentOutOfRangeException(nameof(backdrop));
        processing.Save(options, overwritePolicy, validateBeforeConversion, ignoreLeadingTheForFileOrganization,
            audioBitrates, modern =>
            {
                if (theme.HasValue) modern.SetElementValue("Theme", theme.Value);
                if (backdrop.HasValue) modern.SetElementValue("Backdrop", backdrop.Value);
                if (taggingPatterns != null)
                {
                    modern.Element("TaggingPatterns")?.Remove();
                    modern.Add(new XElement("TaggingPatterns", taggingPatterns.Distinct(StringComparer.Ordinal).Select(p => new XElement("Pattern", p))));
                }
                if (lastTaggingPattern != null) modern.SetElementValue("LastTaggingPattern", lastTaggingPattern);
                if (operationSections != null)
                {
                    var sections = modern.Element("OperationSections");
                    if (sections == null) { sections = new XElement("OperationSections"); modern.Add(sections); }
                    foreach (var sectionState in operationSections) sections.SetElementValue(sectionState.Key, sectionState.Value);
                }
                if (favouriteOperations != null)
                {
                    modern.Element("FavouriteOperations")?.Remove();
                    modern.Add(new XElement("FavouriteOperations", favouriteOperations.Distinct(StringComparer.Ordinal)
                        .Select(id => new XElement("Operation", id))));
                }
                if (settingsWindowSize != null)
                {
                    modern.SetElementValue("SettingsWindowWidth", settingsWindowSize.Width.ToString("R", CultureInfo.InvariantCulture));
                    modern.SetElementValue("SettingsWindowHeight", settingsWindowSize.Height.ToString("R", CultureInfo.InvariantCulture));
                }
                modern.Element("OperationSections")?.Elements().Where(entry => !SectionKeys.Contains(entry.Name)).Remove();
                modern.Element("FavouriteOperations")?.Elements().Where(entry => entry.Name != "Operation").Remove();
                modern.Element("TaggingPatterns")?.Elements().Where(entry => entry.Name != "Pattern").Remove();
            }, PreferenceKeys);
    }

    public IReadOnlyList<string> LoadTaggingPatterns()
    {
        var patterns = new List<string> { FilenameTagPattern.Default };
        if (File.Exists(filename))
            foreach (string value in XDocument.Load(filename).Root?.Element("KaraokeToolkit")?.Element("TaggingPatterns")?.Elements("Pattern").Select(p => p.Value) ?? [])
                try { _ = new FilenameTagPattern(value); if (!patterns.Contains(value, StringComparer.Ordinal)) patterns.Add(value); }
                catch (ArgumentException) { /* An invalid cosmetic history entry must not block settings. */ }
        return patterns;
    }
    public string LoadLastTaggingPattern()
    {
        string? value = File.Exists(filename) ? XDocument.Load(filename).Root?.Element("KaraokeToolkit")?.Element("LastTaggingPattern")?.Value : null;
        return value != null && LoadTaggingPatterns().Contains(value) ? value : FilenameTagPattern.Default;
    }

    public ApplicationBackdrop LoadBackdrop()
    {
        if (!File.Exists(filename)) return ApplicationBackdrop.Automatic;
        string? value = XDocument.Load(filename).Root?.Element("KaraokeToolkit")?.Element("Backdrop")?.Value;
        return Enum.TryParse<ApplicationBackdrop>(value, out var backdrop) && Enum.IsDefined(backdrop)
            ? backdrop : ApplicationBackdrop.Automatic;
    }

    public ApplicationTheme LoadTheme()
    {
        if (!File.Exists(filename))
            return ApplicationTheme.System;
        string? value = XDocument.Load(filename).Root?.Element("KaraokeToolkit")?.Element("Theme")?.Value;
        return Enum.TryParse<ApplicationTheme>(value, out var theme) && Enum.IsDefined(theme)
            ? theme : ApplicationTheme.System;
    }

    public IReadOnlyDictionary<string, bool> LoadOperationSections()
    {
        var result = new Dictionary<string, bool>();
        if (!File.Exists(filename)) return result;
        var sections = XDocument.Load(filename).Root?.Element("KaraokeToolkit")?.Element("OperationSections");
        if (sections == null) return result;
        foreach (var section in sections.Elements())
        {
            if (bool.TryParse(section.Value, out bool expanded))
                result[section.Name.LocalName] = expanded;
        }
        return result;
    }

    public IReadOnlyList<string> LoadFavouriteOperations()
    {
        if (!File.Exists(filename)) return [];
        return XDocument.Load(filename).Root?.Element("KaraokeToolkit")?.Element("FavouriteOperations")?
            .Elements("Operation").Select(element => element.Value).Distinct(StringComparer.Ordinal).ToArray() ?? [];
    }

    public SettingsWindowSize LoadSettingsWindowSize()
    {
        if (!File.Exists(filename)) return new();
        var modern = XDocument.Load(filename).Root?.Element("KaraokeToolkit");
        if (!double.TryParse(modern?.Element("SettingsWindowWidth")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double width) ||
            !double.TryParse(modern?.Element("SettingsWindowHeight")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double height)) return new();
        var size = new SettingsWindowSize(width, height);
        // Cosmetic preferences must not prevent otherwise valid settings from loading.
        return size.IsValid ? size : new();
    }
}
