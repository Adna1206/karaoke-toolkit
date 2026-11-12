using KaraokeToolkit.Core.DiscRipping;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Reporting;
using System.Globalization;
using System.Text;
namespace KaraokeToolkit.Core.FileOperations;

/// <summary>Generates local CUE sheets and track listings without online metadata.</summary>
internal static class CueTextService
{
    internal static IReadOnlyList<string> Write(string folder, string stage, ConversionOptions options, string? preferredExtension = null,
        Action<string>? writing = null)
    {
        string[] files = Directory.GetFiles(folder).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToArray();
        string[] bins = files.Where(p => Path.GetExtension(p).Equals(".bin", StringComparison.OrdinalIgnoreCase)).ToArray();
        string[] tracks = preferredExtension == null
            ? files.Where(p => new[] { ".bin", ".cdg", ".zip" }.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase))
                .DistinctBy(Path.GetFileNameWithoutExtension, StringComparer.OrdinalIgnoreCase).ToArray()
            : files.Where(p => Path.GetExtension(p).Equals(preferredExtension, StringComparison.OrdinalIgnoreCase)).ToArray();
        var outputs = new List<string>();
        var generatedAt = DateTimeOffset.Now;
        string name = MetadataFileNames.FolderName(folder);
        if (bins.Length > 99)
            throw new InvalidDataException("A CUE sheet supports at most 99 tracks.");
        if (bins.Length > 0 && (preferredExtension is null or ".bin"))
        {
            var cue = new StringBuilder(new ReportHeader("CD+G CUE Sheet", generatedAt).ToText("REM "));
            for (int i = 0; i < bins.Length; i++)
            {
                BinConverter.Validate(bins[i]);
                string filename = Path.GetFileName(bins[i]);
                if (filename.IndexOfAny(['"', '\r', '\n']) >= 0)
                    throw new InvalidDataException("Filename cannot be represented in CUE syntax.");
                cue.Append($"FILE \"{filename}\" BINARY\r\n  TRACK {(i + 1).ToString("D2", CultureInfo.InvariantCulture)} CDG\r\n    INDEX 01 00:00:00\r\n");
            }
            string path = Path.Combine(stage, MetadataFileNames.Resolve(options.CueSheetName, name, ".cue"));
            writing?.Invoke("Writing Cue File");
            File.WriteAllText(path, cue.ToString(), new UTF8Encoding(false));
            outputs.Add(path);
        }
        if (tracks.Length > 0)
        {
            string text = new ReportHeader("Track Listing", generatedAt).ToText() + string.Concat(tracks.Select((p, i) => $"{(i + 1).ToString("D2", CultureInfo.InvariantCulture)}. {Path.GetFileNameWithoutExtension(p)}\r\n"));
            string path = Path.Combine(stage, MetadataFileNames.Resolve(options.TrackListingName, name, ".txt"));
            writing?.Invoke("Writing Text File");
            File.WriteAllText(path, text, new UTF8Encoding(false));
            outputs.Add(path);
        }
        return outputs;
    }
}
