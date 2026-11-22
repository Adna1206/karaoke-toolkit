using System.Diagnostics;
using System.Xml.Linq;
using KaraokeToolkit.Core.Audio;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.FileOperations;
using KaraokeToolkit.Core.Recovery;
using KaraokeToolkit.Core.Video;

namespace KaraokeToolkit.Core.Settings;

/// <summary>Reads the versioned profile and serializes its atomic writes across processes.</summary>
public sealed class SettingsStore(string path, bool persistRenameBins = false, bool applyReleaseMigrations = false)
{
    private readonly string filename = Path.GetFullPath(path);

    internal IReadOnlyList<string> RetiredOptionsFiles { private get; init; } =
        UserDataPaths.RetiredOptionsFilesFor(path);

    public ConversionOptions Load() => ReadSnapshot().Options;
    public OverwritePolicy LoadOverwritePolicy() => ReadSnapshot().Overwrite;
    public bool LoadValidateBeforeConversion() => ReadSnapshot().ValidateBeforeConversion;
    public bool LoadIgnoreLeadingTheForFileOrganization() => ReadSnapshot().IgnoreLeadingThe;
    public (RecoveryVerificationMode Mode, bool Resume) LoadRecoveryPreferences()
    {
        var snapshot = ReadSnapshot();
        return (snapshot.RecoveryVerification, snapshot.SaveResumableRun);
    }

    public AudioBitratePreferences LoadAudioBitrates(ConversionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (ReadDocument() is { } document)
            return SettingsXml.Read(document.Root!, persistRenameBins).AudioBitrates;

        var bitrates = new AudioBitratePreferences();
        return AudioEncoderService.SupportedBitrates.Contains(options.AudioBitrate)
            ? bitrates.With(options.AudioEncoder, options.AudioBitrate) : bitrates;
    }

    public string LoadTaggingPattern() => ReadSnapshot().TaggingPattern;
    public XElement? ReadDesktopPreferences() => ReadDocument()?.Root?.Element("Desktop");

    internal SharedSettingsSnapshot ReadSnapshot()
    {
        var document = ReadDocument();
        var root = (document ?? SettingsXml.Create()).Root!;
        if (applyReleaseMigrations && document != null)
            SettingsMigrations.ApplyValues(root, existingProfile: true);
        return SettingsXml.Read(root, persistRenameBins);
    }

    private XDocument? ReadDocument()
    {
        FileStream input;
        try { input = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        using (input)
        {
            var document = XDocument.Load(input);
            SettingsXml.Validate(document);
            return document;
        }
    }

    public void Save(ConversionOptions? options, OverwritePolicy? overwritePolicy = null,
        bool? validateBeforeConversion = null, bool? ignoreLeadingTheForFileOrganization = null,
        AudioBitratePreferences? audioBitrates = null, Action<XElement>? updateAdditionalPreferences = null,
        RecoveryVerificationMode? recoveryVerification = null, bool? saveResumableRun = null, string? taggingPattern = null)
    {
        if (options != null)
        {
            MetadataFileNames.Resolve(options.TrackListingName, "Example folder", ".txt");
            MetadataFileNames.Resolve(options.CueSheetName, "Example folder", ".cue");
            if (!Enum.IsDefined(options.ZipAlgorithm)) throw new ArgumentOutOfRangeException(nameof(options.ZipAlgorithm));
            if (!Enum.IsDefined(options.ZipCompression)) throw new ArgumentOutOfRangeException(nameof(options.ZipCompression));
            VideoOptions.Validate(options);
        }
        if (overwritePolicy.HasValue && !Enum.IsDefined(overwritePolicy.Value))
            throw new ArgumentOutOfRangeException(nameof(overwritePolicy));
        if (recoveryVerification.HasValue && !Enum.IsDefined(recoveryVerification.Value))
            throw new ArgumentOutOfRangeException(nameof(recoveryVerification));
        audioBitrates?.Validate();
        if (taggingPattern != null) SharedSettingCatalog.All["tags.filename-pattern"].Parse(taggingPattern);

        UpdateDocument(root =>
        {
            if (options != null) SettingsXml.WriteOptions(root, options, persistRenameBins);
            if (overwritePolicy.HasValue) SettingsXml.Write(root, "files.overwrite", overwritePolicy.Value);
            if (validateBeforeConversion.HasValue) SettingsXml.Write(root, "files.validate-before-conversion", validateBeforeConversion.Value);
            if (ignoreLeadingTheForFileOrganization.HasValue) SettingsXml.Write(root, "files.ignore-leading-the", ignoreLeadingTheForFileOrganization.Value);
            if (recoveryVerification.HasValue) SettingsXml.Write(root, "recovery.verification", recoveryVerification.Value);
            if (saveResumableRun.HasValue) SettingsXml.Write(root, "recovery.batch-resume", saveResumableRun.Value);
            if (taggingPattern != null) SettingsXml.Write(root, "tags.filename-pattern", taggingPattern);
            if (audioBitrates != null)
            {
                SettingsXml.Write(root, "audio.mp3-bitrate", audioBitrates.Mp3);
                SettingsXml.Write(root, "audio.ogg-bitrate", audioBitrates.Ogg);
                SettingsXml.Write(root, "audio.wma-bitrate", audioBitrates.Wma);
                SettingsXml.Write(root, "audio.aac-bitrate", audioBitrates.Aac);
            }
            updateAdditionalPreferences?.Invoke(SettingsXml.Section(root, "Desktop"));
        });
    }

    internal void UpdateDocument(Action<XElement>? update = null)
    {
        string key = OperatingSystem.IsWindows() ? filename.ToUpperInvariant() : filename;
        using var mutex = new Mutex(false, "KaraokeToolkit-Settings-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key))));
        bool acquired = false;
        try
        {
            try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(5)); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired)
                throw new IOException("Settings are being saved by another process.");

            var existing = ReadDocument();
            var document = existing ?? SettingsXml.Create();
            var root = document.Root!;
            bool changed = applyReleaseMigrations && SettingsMigrations.ApplyValues(root, existing != null);
            update?.Invoke(root);
            if (changed || existing == null || update != null)
                Publish(document);

            if (applyReleaseMigrations)
            {
                try
                {
                    if (SettingsMigrations.CleanupFiles(root, RetiredOptionsFiles))
                        Publish(document);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    Trace.TraceWarning("Settings cleanup is pending: {0}", error.Message);
                }
            }
        }
        finally { if (acquired) mutex.ReleaseMutex(); }
    }

    private void Publish(XDocument document)
    {
        SettingsXml.Sort(SettingsXml.Validate(document));
        Directory.CreateDirectory(Path.GetDirectoryName(filename)!);
        string temporary = filename + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                document.Save(file);
                file.Flush(flushToDisk: true);
            }
            if (File.Exists(filename))
                File.Replace(temporary, filename, filename + ".bak");
            else
                File.Move(temporary, filename);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
