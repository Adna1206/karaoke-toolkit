using KaraokeToolkit.Core.Audio;
using SharpCompress.Archives.Zip;

namespace KaraokeToolkit.Core.Archives;

/// <summary>Reads the audio from a single-track karaoke ZIP without extracting member paths.</summary>
internal static class AudioGainArchive
{
    internal static async Task<ZipMemberReplacement> ExtractAudioAsync(string path, string workspace, CancellationToken token)
    {
        // A stream prevents SharpCompress from looking for other volumes on disk.
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true);
        await using var archive = await ZipArchive.OpenAsyncArchive(input, cancellationToken: token);
        var entries = await archive.EntriesAsync.ToArrayAsync(token);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var members = entries.Select(entry =>
        {
            if (entry.IsEncrypted || entry.IsSplitAfter || entry.VolumeIndexFirst != entry.VolumeIndexLast)
                throw new NotSupportedException("Encrypted or split karaoke ZIPs are not supported.");
            if (!string.IsNullOrEmpty(entry.LinkTarget)) throw new NotSupportedException("Archive links are not supported.");
            string name = ArchiveZipConverter.SafeName(entry.Key ?? "", entry.IsDirectory);
            if (!names.Add(name)) throw new InvalidDataException("Duplicate archive path: " + name);
            return (Entry: entry, Name: name);
        }).Where(member => !member.Entry.IsDirectory).ToArray();
        var audio = members.Where(member => AudioGainService.IsLooseAudio(member.Name) ||
            KaraokeArchive.AudioExtensions.Contains(Path.GetExtension(member.Name), StringComparer.OrdinalIgnoreCase)).ToArray();
        var graphics = members.Where(member => Path.GetExtension(member.Name).Equals(".cdg", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (audio.Length != 1 || graphics.Length != 1 ||
            !string.Equals(Path.GetFileNameWithoutExtension(audio[0].Name), Path.GetFileNameWithoutExtension(graphics[0].Name),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The ZIP must contain one audio file and its matching CDG.");
        if (!AudioGainService.IsLooseAudio(audio[0].Name))
            throw new NotSupportedException($"Audio gain does not support {Path.GetExtension(audio[0].Name)} inside a ZIP.");
        if (graphics[0].Entry.Size <= 0 || graphics[0].Entry.Size % 24 != 0)
            throw new InvalidDataException("CDG must contain complete 24-byte packets.");
        await KaraokeArchive.ReadEntryAsync(graphics[0].Entry, Stream.Null, token);
        string extracted = Path.Combine(workspace, Guid.NewGuid().ToString("N") + Path.GetExtension(audio[0].Name).ToLowerInvariant());
        await using var target = new FileStream(extracted, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, useAsync: true);
        await KaraokeArchive.ReadEntryAsync(audio[0].Entry, target, token);
        return new(audio[0].Name, extracted);
    }
}
