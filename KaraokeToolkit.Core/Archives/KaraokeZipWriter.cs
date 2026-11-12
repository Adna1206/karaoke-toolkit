using SharpCompress.Common;
using SharpCompress.Writers.Zip;
namespace KaraokeToolkit.Core.Archives;

/// <summary>Writes a staged, flat karaoke pair using managed ZIP compression.</summary>
internal static class KaraokeZipWriter
{
    internal static async Task CreateAsync(string audio, string graphics, string zip, ZipCompression compression, CancellationToken token, ZipAlgorithm algorithm = ZipAlgorithm.Deflate)
    {
        token.ThrowIfCancellationRequested();
        var options = CreateOptions(compression, algorithm);
        await using var output = new FileStream(zip, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        using (var writer = new ZipWriter(output, options))
        {
            foreach (string path in new[] { audio, graphics })
            {
                token.ThrowIfCancellationRequested();
                await using var input = File.OpenRead(path);
                if (input.Length <= 0 || input.Length >= uint.MaxValue)
                    throw new InvalidDataException("ZIP inputs must be nonempty and smaller than 4 GiB.");
                using var entry = writer.WriteToStream(Path.GetFileName(path), new ZipWriterEntryOptions { ModificationDateTime = File.GetLastWriteTime(path) });
                await input.CopyToAsync(entry, 65536, token);
            }
        }
        await output.FlushAsync(token);
    }
    internal static ZipWriterOptions CreateOptions(ZipCompression compression, ZipAlgorithm algorithm)
    {
        int level = ZipCompressionMigration.ToLegacy(compression);
        var method = algorithm switch
        {
            ZipAlgorithm.Deflate => level == 0 ? CompressionType.None : CompressionType.Deflate,
            ZipAlgorithm.BZip2 => CompressionType.BZip2,
            ZipAlgorithm.Lzma => CompressionType.LZMA,
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm))
        };
        var options = new ZipWriterOptions(method) { UseZip64 = false, LeaveStreamOpen = true };
        // Levels 0-9 are the Deflate preference. Other algorithms use their library defaults.
        if (algorithm == ZipAlgorithm.Deflate) options.CompressionLevel = level;
        return options;
    }
}
