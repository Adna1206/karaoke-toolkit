using KaraokeToolkit.Core.Audio;
using KaraokeToolkit.Core.Archives;
using KaraokeToolkit.Core.Recovery;
using Action = KaraokeToolkit.Core.Conversion.OperationCatalog.defAction;

namespace KaraokeToolkit.Core.Conversion;

public sealed record SelectionBatch(string Folder, IReadOnlyList<string> Sources);

/// <summary>Expands folders but keeps explicitly selected files scoped to their own source groups.</summary>
public static class InputSelection
{
    public static readonly string[] Extensions = [".zip", ".bin", ".cdg", ".mp3", ".ogg", ".wav", ".flac", ".ape", ".wma", ".aif", ".aiff", ".m4a", ".wv"];
    internal static readonly StringComparer Paths = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public static string[] Normalize(IEnumerable<string> paths)
    {
        var selected = paths.Select(p => Path.TrimEndingDirectorySeparator(Path.GetFullPath(p))).Distinct(Paths).ToArray();
        if (selected.Length == 0)
            throw new ArgumentException("Select at least one file or folder.");
        foreach (string path in selected)
        {
            RunSession.CheckPlainPath(path);
            if (!Directory.Exists(path) && !File.Exists(path))
                throw new FileNotFoundException("Selected input is missing: " + path);
            if (File.Exists(path) && !Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase) && !ArchiveZipConverter.IsInput(path))
                throw new ArgumentException("Unsupported input file: " + path);
            if (path.Split(Path.DirectorySeparatorChar).Any(IsExcludedFolder))
                throw new IOException("Recovery and Damaged folders cannot be used as input: " + path);
        }
        return selected;
    }

    internal static bool IsExcludedFolder(string name) => name.StartsWith(".karaoke-job-", StringComparison.OrdinalIgnoreCase) || name.Equals("Damaged", StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<SelectionBatch> Plan(OperationCatalog.defAction action, IEnumerable<string> paths, bool recurse)
    {
        string[] selected = Normalize(paths);
        bool maintenance = action is Action.Cleanup_Filename or Action.Cleanup_Filenames or Action.Create_Cue_And_Text_Files;
        if (maintenance && selected.Any(File.Exists))
            throw new ArgumentException("Filename cleanup and CUE/TXT creation work on whole folders. Select folders for this action.");
        string extension = action switch
        {
            Action.AudioG_To_Bin or Action.AudioG_To_Zip or Action.LosslessG_To_Mp3G or Action.Karaoke_To_Video => ".cdg",
            Action.Repack_Zip or Action.Zip_To_AudioG or Action.Zip_To_Bin => ".zip",
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
                        action == Action.Karaoke_To_Video ? ConversionService.IsVideoSource(p) :
                        action == Action.Archive_To_Zip ? ArchiveZipConverter.IsInput(p) :
                        Path.GetExtension(p).Equals(extension, StringComparison.OrdinalIgnoreCase)))
                        sources.Add(file);
            }
            else
            {
                string source = path;
                if (extension == ".cdg" && (KaraokeArchive.AudioExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase) || LosslessAudioTranscoder.IsCandidate(path)))
                    source = Directory.GetFiles(Path.GetDirectoryName(path)!).FirstOrDefault(p => Path.GetExtension(p).Equals(".cdg", StringComparison.OrdinalIgnoreCase) && Path.GetFileNameWithoutExtension(p).Equals(Path.GetFileNameWithoutExtension(path), StringComparison.OrdinalIgnoreCase)) ?? Path.ChangeExtension(path, ".cdg");
                else if (action == Action.Karaoke_To_Video ? !ConversionService.IsVideoSource(path) :
                    action == Action.Archive_To_Zip ? !ArchiveZipConverter.IsInput(path) :
                    !Path.GetExtension(path).Equals(extension, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException($"This action requires {extension} inputs: {path}");
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
