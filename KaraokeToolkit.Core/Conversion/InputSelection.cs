using KaraokeToolkit.Core.Execution;
using KaraokeToolkit.Core.Audio;
using KaraokeToolkit.Core.Archives;
using KaraokeToolkit.Core.Recovery;
using Action = KaraokeToolkit.Core.Execution.OperationId;

namespace KaraokeToolkit.Core.Conversion;

public sealed record SelectionBatch(string Folder, IReadOnlyList<string> Sources);

/// <summary>Expands folders but keeps explicitly selected files scoped to their own source groups.</summary>
public static class InputSelection
{
    public static readonly string[] Extensions = [".zip", ".bin", ".cdg", ".mp3", ".ogg", ".wav", ".flac", ".ape", ".wma", ".aif", ".aiff", ".m4a", ".wv"];
    internal static readonly StringComparer Paths = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>Returns distinct absolute inputs after rejecting missing, unsupported or protected paths.</summary>
    /// <remarks>Uses platform-specific path comparison; does not expand folders or modify files.</remarks>
    public static string[] Normalize(IEnumerable<string> paths)
        => Normalize(paths, allowKma: false);

    public static string[] Normalize(IEnumerable<string> paths, bool allowKma)
    {
        var selected = paths.Select(p => Path.TrimEndingDirectorySeparator(Path.GetFullPath(p))).Distinct(Paths).ToArray();
        if (selected.Length == 0)
            throw new ArgumentException("Select at least one file or folder.");
        foreach (string path in selected)
        {
            RunSession.CheckPlainPath(path);
            if (!Directory.Exists(path) && !File.Exists(path))
                throw new FileNotFoundException("Selected input is missing: " + path);
            string extension = Path.GetExtension(path);
            if (File.Exists(path) && !Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase) && !ArchiveZipConverter.IsInput(path) &&
                !(allowKma && extension.Equals(".kma", StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("Unsupported input file: " + path);
            if (path.Split(Path.DirectorySeparatorChar).Any(IsExcludedFolder))
                throw new IOException("Recovery and Damaged folders cannot be used as input: " + path);
        }
        return selected;
    }

    internal static bool IsExcludedFolder(string name) => name.StartsWith(".karaoke-job-", StringComparison.OrdinalIgnoreCase) || name.Equals("Damaged", StringComparison.OrdinalIgnoreCase);

    /// <summary>Extensions accepted for explicit conversion file selections. An empty list means folders only.</summary>
    public static IReadOnlyList<string> ConversionFileExtensions(OperationId action) => action switch
    {
        Action.ZipToBin or Action.ZipToAudioG or Action.RepackZip => [".zip"],
        Action.BinToZip or Action.BinToAudioG => [".bin"],
        Action.KmaToMp3G => [".kma"],
        Action.AudioGToBin or Action.AudioGToZip or Action.LosslessGToMp3G => [".cdg", .. KaraokeArchive.AudioExtensions],
        Action.KaraokeToVideo => [".bin", ".zip", ".cdg", .. KaraokeArchive.AudioExtensions],
        Action.ArchiveToZip => ArchiveZipConverter.Extensions,
        Action.CleanUpFilenames or Action.AddTrackIdSeparator or Action.RemoveTrackIdSeparator or Action.CreateCueAndTextFiles => [],
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };

    /// <summary>Builds per-folder source groups for an operation, expanding folders within the requested scope.</summary>
    /// <remarks>Explicit audio/CDG selections map to their source group rather than selecting every sibling.
    /// Folder-only maintenance rejects individual file selections. Planning does not publish outputs.</remarks>
    public static IReadOnlyList<SelectionBatch> Plan(OperationId action, IEnumerable<string> paths, bool recurse)
    {
        string[] selected = Normalize(paths, allowKma: action == Action.KmaToMp3G);
        var fileExtensions = ConversionFileExtensions(action);
        bool maintenance = fileExtensions.Count == 0;
        if (maintenance && selected.Any(File.Exists))
            throw new ArgumentException("Filename cleanup and CUE/TXT creation work on whole folders. Select folders for this action.");
        string extension = action switch
        {
            Action.AudioGToBin or Action.AudioGToZip or Action.LosslessGToMp3G or Action.KaraokeToVideo => ".cdg",
            Action.RepackZip or Action.ZipToAudioG or Action.ZipToBin => ".zip",
            Action.KmaToMp3G => ".kma",
            _ => ".bin"
        };
        var sources = new HashSet<string>(Paths);
        foreach (string path in selected)
        {
            if (Directory.Exists(path))
            {
                foreach (string folder in ConversionService.EnumerateFolders(path, recurse))
                    if (maintenance)
                        sources.Add(folder);
                    else
                    foreach (string file in Directory.GetFiles(folder).Where(p =>
                        action == Action.KaraokeToVideo ? ConversionService.IsVideoSource(p) :
                        action == Action.ArchiveToZip ? ArchiveZipConverter.IsInput(p) :
                        Path.GetExtension(p).Equals(extension, StringComparison.OrdinalIgnoreCase)))
                        sources.Add(file);
            }
            else
            {
                if (!fileExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                    throw new ArgumentException($"This action requires {string.Join(", ", fileExtensions)} inputs: {path}");
                string source = path;
                if (extension == ".cdg" && (KaraokeArchive.AudioExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase) || LosslessAudioTranscoder.IsCandidate(path)))
                    source = Directory.GetFiles(Path.GetDirectoryName(path)!).FirstOrDefault(p => Path.GetExtension(p).Equals(".cdg", StringComparison.OrdinalIgnoreCase) && Path.GetFileNameWithoutExtension(p).Equals(Path.GetFileNameWithoutExtension(path), StringComparison.OrdinalIgnoreCase)) ?? Path.ChangeExtension(path, ".cdg");
                sources.Add(source);
            }
        }
        return sources.Order(Paths).GroupBy(p => maintenance ? p : Path.GetDirectoryName(p)!, Paths)
            .Select(group => new SelectionBatch(group.Key, group.ToArray())).ToArray();
    }

    internal static IEnumerable<(string File, string Root)> Media(IEnumerable<string> paths, bool recurse)
    {
        var selected = Normalize(paths);
        var files = new Dictionary<string, string>(Paths);
        // Broad folder selections establish the relative paths used for quarantine.
        foreach (string root in selected.Where(Directory.Exists).OrderBy(p => p.Length))
            foreach (string folder in ConversionService.EnumerateFolders(root, recurse))
                foreach (string file in Directory.GetFiles(folder).Where(p => Extensions.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase)))
                    files.TryAdd(file, root);
        foreach (string file in selected.Where(File.Exists))
            files.TryAdd(file, Path.GetDirectoryName(file)!);
        return files.OrderBy(p => p.Key, Paths).Select(p => (p.Key, p.Value));
    }
}
