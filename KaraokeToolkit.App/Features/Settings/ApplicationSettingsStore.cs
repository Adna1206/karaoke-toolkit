using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Xml.Linq;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Settings;

namespace KaraokeToolkit.App.Features.Settings;

/// <summary>Composes processing settings with desktop preferences in the existing settings file.</summary>
/// <remarks>UI entries keep their original XML names so existing installations retain their preferences.
/// All entries are published together using Core's atomic settings transaction.</remarks>
public sealed class ApplicationSettingsStore(string path, bool persistRenameBins = false)
{
    private readonly string filename = Path.GetFullPath(path);
    private readonly SettingsStore processing = new(path, persistRenameBins);

    public ConversionOptions Load() => processing.Load();
    public OverwritePolicy LoadOverwritePolicy() => processing.LoadOverwritePolicy();
    public bool LoadValidateBeforeConversion() => processing.LoadValidateBeforeConversion();
    public bool LoadIgnoreLeadingTheForFileOrganization() => processing.LoadIgnoreLeadingTheForFileOrganization();
    public AudioBitratePreferences LoadAudioBitrates(ConversionOptions options) => processing.LoadAudioBitrates(options);

    public void Save(ConversionOptions options, OverwritePolicy? overwritePolicy = null, ApplicationTheme? theme = null,
        bool? validateBeforeConversion = null, bool? ignoreLeadingTheForFileOrganization = null,
        AudioBitratePreferences? audioBitrates = null, IReadOnlyDictionary<string, bool>? operationSections = null,
        IReadOnlyList<string>? favouriteOperations = null, SettingsWindowSize? settingsWindowSize = null,
        ApplicationBackdrop? backdrop = null)
    {
        if (settingsWindowSize is { IsValid: false }) throw new ArgumentOutOfRangeException(nameof(settingsWindowSize));
        if (theme.HasValue && !Enum.IsDefined(theme.Value)) throw new ArgumentOutOfRangeException(nameof(theme));
        if (backdrop.HasValue && !Enum.IsDefined(backdrop.Value)) throw new ArgumentOutOfRangeException(nameof(backdrop));
        processing.Save(options, overwritePolicy, validateBeforeConversion, ignoreLeadingTheForFileOrganization,
            audioBitrates, modern =>
            {
                if (theme.HasValue) modern.SetElementValue("Theme", theme.Value);
                if (backdrop.HasValue) modern.SetElementValue("Backdrop", backdrop.Value);
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
            });
    }

    public ApplicationBackdrop LoadBackdrop()
    {
        if (!File.Exists(filename)) return ApplicationBackdrop.Automatic;
        string? value = XDocument.Load(filename).Root?.Element("KaraokeToolkit")?.Element("Backdrop")?.Value;
        if (value == null) return ApplicationBackdrop.Automatic;
        if (!Enum.TryParse<ApplicationBackdrop>(value, out var backdrop) || !Enum.IsDefined(backdrop))
            throw new InvalidDataException("Invalid backdrop in settings.");
        return backdrop;
    }

    public ApplicationTheme LoadTheme()
    {
        if (!File.Exists(filename))
            return ApplicationTheme.System;
        string? value = XDocument.Load(filename).Root?.Element("KaraokeToolkit")?.Element("Theme")?.Value;
        if (value == null)
            return ApplicationTheme.System;
        if (!Enum.TryParse<ApplicationTheme>(value, out var theme) || !Enum.IsDefined(theme))
            throw new InvalidDataException("Invalid theme in settings.");
        return theme;
    }

    public IReadOnlyDictionary<string, bool> LoadOperationSections()
    {
        var result = new Dictionary<string, bool>();
        if (!File.Exists(filename)) return result;
        var sections = XDocument.Load(filename).Root?.Element("KaraokeToolkit")?.Element("OperationSections");
        if (sections == null) return result;
        foreach (var section in sections.Elements())
        {
            if (!bool.TryParse(section.Value, out bool expanded)) throw new InvalidDataException("Invalid operation section preference.");
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
