using System.Buffers;

namespace KaraokeToolkit.Core.Recovery;

/// <summary>Copies into a new staging file and fingerprints the source bytes as they are copied.</summary>
internal static class FileStaging
{
    internal static Task<string> CopyInputAsync(string source, string target, RecoveryVerificationMode mode, CancellationToken token) =>
        CopyCoreAsync(source, target, mode, preserveWriteTime: mode == RecoveryVerificationMode.MetadataOnly, useAsync: true, token);

    // Grouping runs on a background worker and flushes staged files before saving a prepared-copy receipt.
    internal static string CopyForGrouping(string source, string target, RecoveryVerificationMode mode) =>
        CopyCoreAsync(source, target, mode, preserveWriteTime: true, useAsync: false, CancellationToken.None).GetAwaiter().GetResult();

    private static async Task<string> CopyCoreAsync(string source, string target, RecoveryVerificationMode mode,
        bool preserveWriteTime, bool useAsync, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        DateTime? writeTime = preserveWriteTime ? File.GetLastWriteTimeUtc(source) : null;
        using var digest = RecoveryFingerprint.IsContentVerified(mode) ? RecoveryFingerprint.CreateContentHash(mode) : null;
        string? metadata = digest == null ? RecoveryFingerprint.Hash(source, mode) : null;
        using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync))
        using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, useAsync))
        {
            if (digest == null)
            {
                if (useAsync) await input.CopyToAsync(output, token).ConfigureAwait(false);
                else input.CopyTo(output);
            }
            else
            {
                byte[] buffer = ArrayPool<byte>.Shared.Rent(65536);
                try
                {
                    while (true)
                    {
                        int count = useAsync
                            ? await input.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)
                            : input.Read(buffer);
                        if (count == 0) break;
                        digest.Append(buffer.AsSpan(0, count));
                        if (useAsync) await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                        else output.Write(buffer.AsSpan(0, count));
                    }
                }
                finally { ArrayPool<byte>.Shared.Return(buffer); }
            }

            if (useAsync) await output.FlushAsync(token).ConfigureAwait(false);
            else output.Flush(flushToDisk: true);
        }
        if (writeTime is { } timestamp) File.SetLastWriteTimeUtc(target, timestamp);
        return digest?.GetHex() ?? metadata!;
    }
}
