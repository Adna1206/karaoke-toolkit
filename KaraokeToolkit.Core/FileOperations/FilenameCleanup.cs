using KaraokeToolkit.Core.Archives;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Execution;
using KaraokeToolkit.Core.Naming;
using KaraokeToolkit.Core.Recovery;
using System.Globalization;
namespace KaraokeToolkit.Core.FileOperations;

/// <summary>Normalizes local karaoke filenames as a group, retaining audio/CDG basename pairing.</summary>
internal static class FilenameCleanup
{
    internal static string FormatTrackId(string filename, bool addSeparator)
    {
        if (LeadingTrackId.Parse(Path.GetFileNameWithoutExtension(filename)) is not { } id ||
            id.Separator is not ("" or "-"))
            return filename;

        bool hasSeparator = id.Separator.Length != 0;
        if (addSeparator)
            return hasSeparator ? filename : filename.Insert(id.SeparatorIndex, "-");
        return hasSeparator ? filename.Remove(id.SeparatorIndex, id.Separator.Length) : filename;
    }

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
    internal static async Task<ConversionFileResult> RunAsync(string folder, OperationId operation, CancellationToken token, bool preview = false)
    {
        var files = Directory.GetFiles(folder).Where(p => KaraokeArchive.AudioExtensions.Concat([".cdg", ".bin", ".zip"]).Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase)).ToArray();
        var destinations = files.Select(p => Path.Combine(folder, Rename(Path.GetFileName(p)))).ToArray();
        if (destinations.Distinct(StringComparer.OrdinalIgnoreCase).Count() != destinations.Length)
            throw new IOException("Cleanup would give multiple files the same name; no files were renamed.");
        var changed = files.Select((p, i) => (Source: p, Destination: destinations[i])).Where(p => p.Source != p.Destination).ToArray();
        if (changed.Length == 0)
            return new(folder, ConversionStatus.Skipped, [], operation == OperationId.CleanUpFilenames
                ? "Names already clean." : "No filenames need a track ID separator change.");
        if (changed.Any(pair => Directory.Exists(pair.Destination)))
            throw new IOException("A rename destination is an existing folder; no files were renamed.");
        if (preview) return new(folder, ConversionStatus.Succeeded, changed.Select(p => p.Destination).ToArray(), $"Planned rename of {changed.Length} files.");
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

        string Rename(string name) => operation switch
        {
            OperationId.CleanUpFilenames => Clean(name),
            OperationId.AddTrackIdSeparator => FormatTrackId(name, addSeparator: true),
            OperationId.RemoveTrackIdSeparator => FormatTrackId(name, addSeparator: false),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
    }
}
