using KaraokeToolkit.Core.Archives;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Recovery;
using System.Globalization;
namespace KaraokeToolkit.Core.FileOperations;

/// <summary>Normalizes local karaoke filenames as a group, retaining audio/CDG basename pairing.</summary>
internal static class FilenameCleanup
{
    internal static string Clean(string filename)
    {
        string extension = Path.GetExtension(filename).ToLowerInvariant();
        string stem = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(Path.GetFileNameWithoutExtension(filename).Replace('_', ' ').ToLower(CultureInfo.CurrentCulture));
        int space = stem.IndexOf(' ');
        // Retain the original convention: the first token is an uppercase disc identifier.
        stem = space < 0 ? stem : stem[..space].ToUpper(CultureInfo.CurrentCulture) + " " + stem[(space + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(stem))
            throw new InvalidDataException("Filename has no usable title.");
        return stem + extension;
    }
    internal static async Task<ConversionFileResult> RunAsync(string folder, CancellationToken token)
    {
        var files = Directory.GetFiles(folder).Where(p => KaraokeArchive.AudioExtensions.Concat([".cdg", ".bin", ".zip"]).Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase)).ToArray();
        var destinations = files.Select(p => Path.Combine(folder, Clean(Path.GetFileName(p)))).ToArray();
        if (destinations.Distinct(StringComparer.OrdinalIgnoreCase).Count() != destinations.Length)
            throw new IOException("Cleanup would give multiple files the same name; no files were renamed.");
        var changed = files.Select((p, i) => (Source: p, Destination: destinations[i])).Where(p => p.Source != p.Destination).ToArray();
        if (changed.Length == 0)
            return new(folder, ConversionStatus.Skipped, [], "Names already clean.");
        using var transaction = new FileTransaction(folder);
        // Copy into separate input directory, then stage final names; this also handles case-only renames.
        var staged = new List<string>();
        foreach (var pair in changed)
        {
            token.ThrowIfCancellationRequested();
            string copy = await transaction.CopyInputAsync(pair.Source, token);
            string renamed = Path.Combine(transaction.Workspace, Guid.NewGuid().ToString("N"));
            File.Move(copy, renamed);
            staged.Add(renamed);
        }
        for (int i = 0; i < staged.Count; i++)
        {
            string output = Path.Combine(transaction.Workspace, Path.GetFileName(changed[i].Destination));
            File.Move(staged[i], output);
            staged[i] = output;
        }
        var published = transaction.Commit(staged, changed.Select(p => p.Source).ToArray(), OverwritePolicy.Fail, token);
        return new(folder, ConversionStatus.Succeeded, published, $"Renamed {changed.Length} files.");
    }
}
