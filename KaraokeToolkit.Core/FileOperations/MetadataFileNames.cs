namespace KaraokeToolkit.Core.FileOperations;

/// <summary>Expands generated listing names into safe, portable filenames.</summary>
public static class MetadataFileNames
{
    public const string DefaultTemplate = "{folder}";

    /// <summary>Resolves a TXT or CUE filename using a folder's leaf name.</summary>
    public static string Resolve(string? template, string folderName, string extension)
    {
        if (extension is not (".txt" or ".cue")) throw new ArgumentException("Expected .txt or .cue.", nameof(extension));
        if (string.IsNullOrWhiteSpace(template)) throw new ArgumentException("Enter a filename or use {folder}.");
        string stem = template.Trim();
        if (stem.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) stem = stem[..^extension.Length];
        if (stem.Replace(DefaultTemplate, "", StringComparison.Ordinal).IndexOfAny(['{', '}']) >= 0)
            throw new ArgumentException("The only supported placeholder is {folder}.");

        stem = stem.Replace(DefaultTemplate, folderName.Length == 0 ? "tracks" : folderName, StringComparison.Ordinal);
        return ValidateAndAppendExtension(stem, extension);
    }

    internal static string ValidateAndAppendExtension(string stem, string extension)
    {
        if (stem.Length == 0 || stem.EndsWith('.') || stem.EndsWith(' ') ||
            stem.IndexOfAny(['<', '>', ':', '"', '/', '\\', '|', '?', '*']) >= 0 || stem.Any(char.IsControl))
            throw new ArgumentException("Use a filename without path separators, reserved characters, or trailing dots or spaces.");
        string device = stem.Split('.', 2)[0].TrimEnd().ToUpperInvariant();
        if (device is "CON" or "PRN" or "AUX" or "NUL" ||
            (device.Length == 4 && (device.StartsWith("COM", StringComparison.Ordinal) || device.StartsWith("LPT", StringComparison.Ordinal)) &&
                "123456789¹²³".Contains(device[3])))
            throw new ArgumentException("Choose a filename other than a reserved Windows device name.");
        string filename = stem + extension;
        if (filename.Length > 255 || System.Text.Encoding.UTF8.GetByteCount(filename) > 255)
            throw new ArgumentException("The generated filename is too long.");
        return filename;
    }

    internal static string FolderName(string folder) => Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)));
}
