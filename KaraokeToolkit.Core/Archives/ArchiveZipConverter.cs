using KaraokeToolkit.Core.Conversion;
using System.Security.Cryptography;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using SharpCompress.Writers.Zip;
namespace KaraokeToolkit.Core.Archives;

/// <summary>Streams supported archive contents into a verified ZIP, without extracting archive paths to disk.</summary>
public static class ArchiveZipConverter
{
    public static readonly string[] Extensions = [".7z", ".rar", ".tar", ".gz", ".tgz", ".bz2", ".tbz", ".tbz2", ".xz", ".txz", ".lz", ".zst"];
    public static bool IsInput(string path) => Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    internal static string OutputName(string source)
    {
        string name = Path.GetFileNameWithoutExtension(source);
        if (name.EndsWith(".tar", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        return name + ".zip";
    }

    internal static async Task ConvertAsync(string source, string destination, ConversionOptions options, CancellationToken token, bool repackZip = false)
    {
        token.ThrowIfCancellationRequested();
        // Streams intentionally prevent auto-discovery of unselected archive volumes.
        using var input = File.OpenRead(source);
        using var archive = Path.GetExtension(source).Equals(".7z", StringComparison.OrdinalIgnoreCase)
            ? ArchiveFactory.OpenArchive(input) : null;
        // Read ZIP central metadata before opening each member: SharpCompress then
        // replaces it with local-header metadata, and both must agree with the payload.
        using var sourceZip = repackZip ? SharpCompress.Archives.Zip.ZipArchive.OpenArchive(input) : null;
        using var zipEntries = sourceZip?.Entries.GetEnumerator();
        using var reader = repackZip ? null : archive != null ? archive.ExtractAllEntries() : ReaderFactory.OpenReader(input);
        if (reader?.Type == ArchiveType.Zip) throw new NotSupportedException("Input is already a ZIP archive.");
        var expected = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var fileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directoryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.ReadWrite))
        using (var writer = new ZipWriter(output, KaraokeZipWriter.CreateOptions(options.ZipCompression, options.ZipAlgorithm)))
        {
            while (repackZip ? zipEntries!.MoveNext() : reader!.MoveToNextEntry())
            {
                token.ThrowIfCancellationRequested();
                IEntry entry = repackZip ? zipEntries!.Current : reader!.Entry;
                if (entry.IsEncrypted || entry.IsSplitAfter || entry.VolumeIndexFirst != entry.VolumeIndexLast)
                    throw new NotSupportedException("Encrypted or split archives are not supported.");
                if (!string.IsNullOrEmpty(entry.LinkTarget)) throw new NotSupportedException("Archive links are not supported.");
                string rawName = string.IsNullOrEmpty(entry.Key) ? Path.GetFileNameWithoutExtension(source) : entry.Key;
                // TAR commonly prefixes relative names with ./ and may contain a root directory entry.
                if (entry.IsDirectory && rawName.Replace('\\', '/').TrimEnd('/') == ".") continue;
                string name = SafeName(rawName, entry.IsDirectory);
                string canonical = name.TrimEnd('/');
                if (!expected.TryAdd(name, null)) throw new InvalidDataException("Duplicate archive path: " + name);
                string[] parts = canonical.Split('/');
                for (int i = 1; i < parts.Length; i++)
                {
                    string parent = string.Join('/', parts.Take(i));
                    if (fileNames.Contains(parent)) throw new InvalidDataException("Conflicting archive paths: " + name);
                    directoryNames.Add(parent);
                }
                if (entry.IsDirectory)
                {
                    if (fileNames.Contains(canonical)) throw new InvalidDataException("Conflicting archive paths: " + name);
                    directoryNames.Add(canonical);
                    writer.WriteDirectory(name, entry.LastModifiedTime);
                    continue;
                }
                if (directoryNames.Contains(canonical)) throw new InvalidDataException("Conflicting archive paths: " + name);
                fileNames.Add(canonical);
                long advertisedSize = entry.Size;
                long advertisedCrc = entry.Crc;
                if (advertisedSize >= uint.MaxValue) throw new NotSupportedException("Archive members must be smaller than 4 GiB.");
                using var member = repackZip ? zipEntries!.Current.OpenEntryStream() : reader!.OpenEntryStream();
                using var target = writer.WriteToStream(name, new ZipWriterEntryOptions { ModificationDateTime = entry.LastModifiedTime });
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                byte[] buffer = new byte[65536];
                long size = 0;
                uint crc = uint.MaxValue;
                int count;
                while ((count = await member.ReadAsync(buffer, token)) != 0)
                {
                    size += count;
                    if (size >= uint.MaxValue || (advertisedSize > 0 && size > advertisedSize))
                        throw new InvalidDataException("Archive member exceeds its supported size.");
                    hash.AppendData(buffer, 0, count);
                    for (int i = 0; i < count; i++)
                    {
                        crc ^= buffer[i];
                        for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0);
                    }
                    await target.WriteAsync(buffer.AsMemory(0, count), token);
                }
                if ((repackZip || advertisedSize > 0) && size != advertisedSize) throw new InvalidDataException("Truncated archive member.");
                // TAR has header checksums, not payload CRCs. Some formats omit a per-member CRC.
                if (repackZip ? ~crc != advertisedCrc || ~crc != entry.Crc :
                    reader!.Type is ArchiveType.Rar or ArchiveType.SevenZip && advertisedCrc != 0 && ~crc != advertisedCrc)
                    throw new InvalidDataException("Archive member CRC check failed.");
                expected[name] = Convert.ToHexString(hash.GetHashAndReset());
            }
        }
        if (expected.Count == 0) throw new InvalidDataException("Archive contains no entries.");
        using var zip = SharpCompress.Archives.Zip.ZipArchive.OpenArchive(destination);
        if (zip.Entries.Count() != expected.Count) throw new InvalidDataException("ZIP entry count differs from the source.");
        foreach (var entry in zip.Entries)
        {
            token.ThrowIfCancellationRequested();
            if (!expected.TryGetValue(entry.Key!, out string? hash)) throw new InvalidDataException("Unexpected ZIP member.");
            if (hash == null)
            {
                if (!entry.IsDirectory) throw new InvalidDataException("ZIP directory was not preserved.");
                continue;
            }
            using var stream = entry.OpenEntryStream();
            if (Convert.ToHexString(await SHA256.HashDataAsync(stream, token)) != hash)
                throw new InvalidDataException("ZIP payload differs from the source.");
        }
    }

    private static string SafeName(string name, bool directory)
    {
        name = name.Replace('\\', '/');
        if (name.StartsWith('/') || name.Contains(':') || name.Any(char.IsControl))
            throw new InvalidDataException("Unsafe archive path: " + name);
        while (name.StartsWith("./", StringComparison.Ordinal)) name = name[2..];
        string[] parts = name.TrimEnd('/').Split('/');
        if (parts.Any(p => p is "" or "." or ".." || p.EndsWith(' ') || p.EndsWith('.')))
            throw new InvalidDataException("Unsafe archive path: " + name);
        return string.Join('/', parts) + (directory ? "/" : "");
    }
}
