using KaraokeToolkit.Core.Recovery;
using SharpCompress.Archives;
using SharpCompress.Archives.Zip;
using System.Security.Cryptography;
namespace KaraokeToolkit.Core.Archives;

/// <summary>Reads a single karaoke pair without trusting archive paths or silently choosing among songs.</summary>
internal static class KaraokeArchive
{
    internal static readonly string[] AudioExtensions = [".ogg", ".mp3", ".wav", ".flac", ".ape", ".wma", ".aif", ".aiff", ".m4a", ".wv"];
    internal static void ValidateGraphics(string path)
    {
        long length = new FileInfo(path).Length;
        if (length == 0 || length % 24 != 0)
            throw new InvalidDataException("CDG must contain complete 24-byte packets.");
    }
    internal static async Task<(string Audio, string Graphics)> ExtractAsync(string zipPath, CancellationToken token)
    {
        using var zip = ZipArchive.OpenArchive(zipPath);
        foreach (var entry in zip.Entries)
        {
            string normalized = entry.Key!.Replace('\\', '/');
            if (normalized.StartsWith('/') || normalized.Contains(':') || normalized.Split('/').Contains(".."))
                throw new InvalidDataException("Archive contains an unsafe entry path.");
        }
        var graphics = zip.Entries.Where(e => Path.GetExtension(e.Key!).Equals(".cdg", StringComparison.OrdinalIgnoreCase)).ToArray();
        var audio = zip.Entries.Where(e => AudioExtensions.Contains(Path.GetExtension(e.Key!), StringComparer.OrdinalIgnoreCase)).ToArray();
        if (zip.Entries.Count(e => !e.IsDirectory) != 2 || graphics.Length != 1 || audio.Length != 1 ||
            !Path.GetFileNameWithoutExtension(graphics[0].Key!).Equals(Path.GetFileNameWithoutExtension(audio[0].Key!), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("ZIP must contain exactly one matching audio/CDG pair.");
        // Flatten to the ZIP basename, matching the original public naming convention.
        string cdg = Path.ChangeExtension(zipPath, ".cdg");
        string music = Path.ChangeExtension(zipPath, Path.GetExtension(audio[0].Key!).ToLowerInvariant());
        await ExtractEntryAsync(graphics[0], cdg, token);
        await ExtractEntryAsync(audio[0], music, token);
        ValidateGraphics(cdg);
        return (music, cdg);
    }
    private static async Task ExtractEntryAsync(IArchiveEntry entry, string output, CancellationToken token)
    {
        await using var target = new FileStream(output, FileMode.CreateNew, FileAccess.Write);
        await ReadEntryAsync(entry, target, token);
    }
    internal static async Task ReadEntryAsync(IArchiveEntry entry, Stream target, CancellationToken token)
    {
        if (entry.IsEncrypted) throw new NotSupportedException("Encrypted ZIP members are not supported.");
        if (entry.Size <= 0 || entry.Size > 4L * 1024 * 1024 * 1024)
            throw new InvalidDataException("Archive member is empty or exceeds the 4 GiB limit.");
        // Opening a member replaces SharpCompress's central-directory metadata with
        // its local header. Retain both so a corrupt central CRC cannot be hidden.
        long expectedSize = entry.Size;
        long expectedCrc = entry.Crc;
        await using var input = entry.OpenEntryStream();
        byte[] buffer = new byte[65536];
        long written = 0;
        uint crc = uint.MaxValue;
        int count;
        while ((count = await input.ReadAsync(buffer, token)) != 0)
        {
            written += count;
            for (int i = 0; i < count; i++)
            {
                crc ^= buffer[i];
                for (int bit = 0; bit < 8; bit++)
                    crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0);
            }
            if (written > expectedSize)
                throw new InvalidDataException("Archive member exceeds its advertised length.");
            await target.WriteAsync(buffer.AsMemory(0, count), token);
        }
        if (~crc != expectedCrc || ~crc != entry.Crc)
            throw new InvalidDataException("Archive member CRC check failed.");
        if (written != expectedSize)
            throw new InvalidDataException("Truncated archive member.");
    }
    internal static void VerifyCreated(string zipPath, string audio, string graphics)
    {
        using var zip = ZipArchive.OpenArchive(zipPath);
        if (zip.Entries.Count() != 2)
            throw new InvalidDataException("Output ZIP does not contain exactly two files.");
        foreach (string input in new[] { audio, graphics })
        {
            var entry = zip.Entries.SingleOrDefault(e => e.Key! == Path.GetFileName(input))
                ?? throw new InvalidDataException("Output ZIP is missing " + Path.GetFileName(input));
            using var stream = entry.OpenEntryStream();
            if (Convert.ToHexString(SHA256.HashData(stream)) != FileTransaction.Hash(input))
                throw new InvalidDataException("Output ZIP payload differs from its source.");
        }
    }
}


