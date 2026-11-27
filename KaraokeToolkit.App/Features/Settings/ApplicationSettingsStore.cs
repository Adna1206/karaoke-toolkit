using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Xml.Linq;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Naming;
using KaraokeToolkit.Core.Settings;
using KaraokeToolkit.Core.Recovery;
using KaraokeToolkit.Core.Tagging;

namespace KaraokeToolkit.App.Features.Settings;

/// <summary>Composes desktop preferences with the versioned processing profile.</summary>
public sealed class ApplicationSettingsStore(string path, bool persistRenameBins = false, bool applyReleaseMigrations = false)
{
    private readonly SettingsStore processing = new(path, persistRenameBins, applyReleaseMigrations);

    public ConversionOptions Load() => processing.Load();
    public OverwritePolicy LoadOverwritePolicy() => processing.LoadOverwritePolicy();
    public bool LoadValidateBeforeConversion() => processing.LoadValidateBeforeConversion();
    public bool LoadIgnoreLeadingTheForFileOrganization() => processing.LoadIgnoreLeadingTheForFileOrganization();
    public AudioBitratePreferences LoadAudioBitrates(ConversionOptions options) => processing.LoadAudioBitrates(options);

    internal (bool Remember, string? Folder) LoadInputFolderPreference()
    {
        var desktop = processing.ReadDesktopPreferences();
        bool remember = bool.TryParse(desktop?.Element("RememberInputFolder")?.Value, out bool enabled) && enabled;
        return (remember, remember ? desktop?.Element("LastInputFolder")?.Value : null);
    }

    internal bool LoadCheckForUpdatesOnStartup() => bool.TryParse(
        processing.ReadDesktopPreferences()?.Element("CheckForUpdatesOnStartup")?.Value, out bool enabled) && enabled;

    internal (RecoveryVerificationMode Mode, bool Resume) LoadRecoveryPreferences() => processing.LoadRecoveryPreferences();
    internal RecoveryVerificationMode LoadMoveRecoveryVerification() => processing.LoadMoveRecoveryVerification();
    internal bool LoadCheckForInterruptedRunsOnStartup() => processing.LoadCheckForInterruptedRunsOnStartup();
    internal FileCheckTiming LoadFileCheckTiming() => processing.LoadFileCheckTiming();

    public void Save(ConversionOptions? options = null, OverwritePolicy? overwritePolicy = null, ApplicationTheme? theme = null,
        bool? validateBeforeConversion = null, bool? ignoreLeadingTheForFileOrganization = null,
        AudioBitratePreferences? audioBitrates = null,
        IReadOnlyList<string>? favouriteOperations = null, SettingsWindowSize? settingsWindowSize = null,
        ApplicationBackdrop? backdrop = null, IReadOnlyList<string>? taggingPatterns = null, string? lastTaggingPattern = null,
        bool? rememberInputFolder = null, string? lastInputFolder = null, RecoveryVerificationMode? recoveryVerification = null, bool? saveResumableRun = null,
        bool? checkForUpdatesOnStartup = null, bool? checkForInterruptedRunsOnStartup = null, FileCheckTiming? checkTiming = null,
        RecoveryVerificationMode? moveRecoveryVerification = null)
    {
        if (settingsWindowSize is { IsValid: false }) throw new ArgumentOutOfRangeException(nameof(settingsWindowSize));
        if (theme.HasValue && !Enum.IsDefined(theme.Value)) throw new ArgumentOutOfRangeException(nameof(theme));
        if (backdrop.HasValue && !Enum.IsDefined(backdrop.Value)) throw new ArgumentOutOfRangeException(nameof(backdrop));
        processing.Save(options, overwritePolicy, validateBeforeConversion, ignoreLeadingTheForFileOrganization,
            audioBitrates, Update, recoveryVerification, saveResumableRun, lastTaggingPattern, checkForInterruptedRunsOnStartup, checkTiming,
            moveRecoveryVerification);

        void Update(XElement desktop)
        {
            if (checkForUpdatesOnStartup.HasValue) desktop.SetElementValue("CheckForUpdatesOnStartup", checkForUpdatesOnStartup.Value);
            if (theme.HasValue) desktop.SetElementValue("Theme", theme.Value);
            if (backdrop.HasValue) desktop.SetElementValue("Backdrop", backdrop.Value);
            if (rememberInputFolder.HasValue) desktop.SetElementValue("RememberInputFolder", rememberInputFolder.Value);
            if (rememberInputFolder == false) desktop.Element("LastInputFolder")?.Remove();
            else if (lastInputFolder != null) desktop.SetElementValue("LastInputFolder", lastInputFolder);
            if (taggingPatterns != null)
            {
                desktop.Element("TaggingPatterns")?.Remove();
                desktop.Add(new XElement("TaggingPatterns", taggingPatterns.Distinct(StringComparer.Ordinal).Select(p => new XElement("Pattern", p))));
            }
            if (favouriteOperations != null)
            {
                desktop.Element("FavouriteOperations")?.Remove();
                desktop.Add(new XElement("FavouriteOperations", favouriteOperations.Distinct(StringComparer.Ordinal)
                    .Select(id => new XElement("Operation", id))));
            }
            if (settingsWindowSize != null)
            {
                desktop.SetElementValue("SettingsWindowWidth", settingsWindowSize.Width.ToString("R", CultureInfo.InvariantCulture));
                desktop.SetElementValue("SettingsWindowHeight", settingsWindowSize.Height.ToString("R", CultureInfo.InvariantCulture));
            }
        }
    }

    public IReadOnlyList<string> LoadTaggingPatterns()
    {
        var patterns = new List<string>(KaraokeFilenamePattern.Presets);
        foreach (string value in processing.ReadDesktopPreferences()?.Element("TaggingPatterns")?.Elements("Pattern").Select(p => p.Value) ?? [])
        {
            try { _ = new FilenameTagPattern(value); if (!patterns.Contains(value, StringComparer.Ordinal)) patterns.Add(value); }
            catch (ArgumentException) { /* An invalid cosmetic history entry must not block settings. */ }
        }
        string selected = processing.LoadTaggingPattern();
        if (!patterns.Contains(selected, StringComparer.Ordinal)) patterns.Add(selected);
        return patterns;
    }
    public string LoadLastTaggingPattern() => processing.LoadTaggingPattern();

    public ApplicationBackdrop LoadBackdrop()
    {
        string? value = processing.ReadDesktopPreferences()?.Element("Backdrop")?.Value;
        return Enum.TryParse<ApplicationBackdrop>(value, out var backdrop) && Enum.IsDefined(backdrop)
            ? backdrop : ApplicationBackdrop.Automatic;
    }

    public ApplicationTheme LoadTheme()
    {
        string? value = processing.ReadDesktopPreferences()?.Element("Theme")?.Value;
        return Enum.TryParse<ApplicationTheme>(value, out var theme) && Enum.IsDefined(theme)
            ? theme : ApplicationTheme.System;
    }

    public IReadOnlyList<string> LoadFavouriteOperations() =>
        processing.ReadDesktopPreferences()?.Element("FavouriteOperations")?.Elements("Operation")
            .Select(element => element.Value).Distinct(StringComparer.Ordinal).ToArray() ?? [];

    public SettingsWindowSize LoadSettingsWindowSize()
    {
        var desktop = processing.ReadDesktopPreferences();
        if (!double.TryParse(desktop?.Element("SettingsWindowWidth")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double width) ||
            !double.TryParse(desktop?.Element("SettingsWindowHeight")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double height)) return new();
        var size = new SettingsWindowSize(width, height);
        // Cosmetic preferences must not prevent otherwise valid settings from loading.
        return size.IsValid ? size : new();
    }
}
