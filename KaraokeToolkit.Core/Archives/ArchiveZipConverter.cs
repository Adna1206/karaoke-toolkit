using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Recovery;
using SharpCompress.Archives;
using SharpCompress.Archives.Zip;
using SharpCompress.Common;
using SharpCompress.Readers;
using SharpCompress.Writers.Zip;
namespace KaraokeToolkit.Core.Archives;

internal sealed record ZipMemberReplacement(string Name, string Path);

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

    internal static async Task ConvertAsync(string source, string destination, ConversionOptions options, CancellationToken token,
        bool repackZip = false, RecoveryVerificationMode verification = RecoveryVerificationMode.Sha256,
        ZipMemberReplacement? replacement = null)
    {
        token.ThrowIfCancellationRequested();
        if (replacement != null && !repackZip) throw new ArgumentException("A ZIP member can only be replaced when repacking a ZIP.");
        // Streams intentionally prevent auto-discovery of unselected archive volumes.
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true);
        await using var archive = Path.GetExtension(source).Equals(".7z", StringComparison.OrdinalIgnoreCase)
            ? await ArchiveFactory.OpenAsyncArchive(input, cancellationToken: token) : null;
        // Read ZIP central metadata before opening each member: SharpCompress then
        // replaces it with local-header metadata, and both must agree with the payload.
        await using var sourceZip = repackZip ? await ZipArchive.OpenAsyncArchive(input, cancellationToken: token) : null;
        await using var zipEntries = sourceZip?.EntriesAsync.GetAsyncEnumerator(token);
        await using var reader = repackZip ? null : archive != null
            ? await archive.ExtractAllEntriesAsync() : await ReaderFactory.OpenAsyncReader(input, cancellationToken: token);
        if (reader?.Type == ArchiveType.Zip) throw new NotSupportedException("Input is already a ZIP archive.");
        var expected = new Dictionary<string, MemberFingerprint?>(StringComparer.OrdinalIgnoreCase);
        var fileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directoryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool replaced = false;
        await using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 65536, useAsync: true))
        await using (var writer = await ZipWriter.OpenAsyncWriter(output, KaraokeZipWriter.CreateOptions(options.ZipCompression, options.ZipAlgorithm), token))
        {
            while (repackZip ? await zipEntries!.MoveNextAsync() : await reader!.MoveToNextEntryAsync(token))
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
                    await writer.WriteDirectoryAsync(name, entry.LastModifiedTime, token);
                    continue;
                }
                if (directoryNames.Contains(canonical)) throw new InvalidDataException("Conflicting archive paths: " + name);
                fileNames.Add(canonical);
                bool replaceMember = replacement != null && name.Equals(replacement.Name, StringComparison.Ordinal);
                long advertisedSize = entry.Size;
                long advertisedCrc = entry.Crc;
                await using var member = replaceMember ? File.OpenRead(replacement!.Path) :
                    repackZip ? await zipEntries!.Current.OpenEntryStreamAsync(token) : await reader!.OpenEntryStreamAsync(token);
                if (replaceMember) advertisedSize = member.Length;
                if (advertisedSize >= uint.MaxValue) throw new NotSupportedException("Archive members must be smaller than 4 GiB.");
                using var hash = RecoveryFingerprint.IsContentVerified(verification) ? RecoveryFingerprint.CreateContentHash(verification) : null;
                using var verified = new VerifyingMemberStream(member, advertisedSize, hash);
                await writer.WriteAsync(name, verified, entry.LastModifiedTime, token);
                if ((repackZip || advertisedSize > 0) && verified.BytesRead != advertisedSize) throw new InvalidDataException("Truncated archive member.");
                // TAR has header checksums, not payload CRCs. Some formats omit a per-member CRC.
                if (!replaceMember && (repackZip ? verified.Crc != advertisedCrc || verified.Crc != entry.Crc :
                    reader!.Type is ArchiveType.Rar or ArchiveType.SevenZip && advertisedCrc != 0 && verified.Crc != advertisedCrc))
                    throw new InvalidDataException("Archive member CRC check failed.");
                replaced |= replaceMember;
                expected[name] = new(verified.BytesRead, verified.Crc, hash?.GetHex());
            }
        }
        if (replacement != null && !replaced) throw new InvalidDataException("The audio member was missing when rebuilding the ZIP.");
        if (expected.Count == 0) throw new InvalidDataException("Archive contains no entries.");
        await using var zip = await ZipArchive.OpenAsyncArchive(destination, cancellationToken: token);
        var entries = await zip.EntriesAsync.ToArrayAsync(token);
        if (entries.Length != expected.Count) throw new InvalidDataException("ZIP entry count differs from the source.");
        foreach (var entry in entries)
        {
            token.ThrowIfCancellationRequested();
            if (!expected.TryGetValue(entry.Key!, out var fingerprint)) throw new InvalidDataException("Unexpected ZIP member.");
            if (fingerprint == null)
            {
                if (!entry.IsDirectory) throw new InvalidDataException("ZIP directory was not preserved.");
                continue;
            }
            if (entry.IsDirectory || entry.Size != fingerprint.Size || entry.Crc != fingerprint.Crc)
                throw new InvalidDataException("ZIP payload differs from the source.");
            if (fingerprint.Hash is { } hash)
            {
                await using var stream = await entry.OpenEntryStreamAsync(token);
                if (await RecoveryFingerprint.HashStreamAsync(stream, verification, token) != hash)
                    throw new InvalidDataException("ZIP payload differs from the source.");
            }
            else
                await KaraokeArchive.ReadEntryAsync(entry, Stream.Null, token, allowEmpty: true);
        }
    }

    private sealed record MemberFingerprint(long Size, uint Crc, string? Hash);

    // The async writer pulls the payload. Verify the same bytes as they stream
    // through to the staged ZIP, without buffering an entire archive member.
    private sealed class VerifyingMemberStream(Stream source, long advertisedSize, RecoveryFingerprint.ContentHash? hash) : Stream
    {
        private uint crc = uint.MaxValue;
        internal long BytesRead { get; private set; }
        internal uint Crc => ~crc;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int count = await source.ReadAsync(buffer, cancellationToken);
            BytesRead += count;
            if (BytesRead >= uint.MaxValue || (advertisedSize > 0 && BytesRead > advertisedSize))
                throw new InvalidDataException("Archive member exceeds its supported size.");
            var bytes = buffer.Span[..count];
            hash?.Append(bytes);
            foreach (byte value in bytes)
            {
                crc ^= value;
                for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0);
            }
            return count;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
    }

    internal static string SafeName(string name, bool directory)
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
