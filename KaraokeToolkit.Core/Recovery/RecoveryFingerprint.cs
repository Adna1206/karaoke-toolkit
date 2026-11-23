using System.Buffers;
using System.IO.Hashing;
using System.Security.Cryptography;

namespace KaraokeToolkit.Core.Recovery;

public enum RecoveryVerificationMode
{
    Sha256,
    XxHash128,
    MetadataOnly,
    None
}

public enum FileCheckTiming
{
    BeforeBatch,
    BeforeItem
}

internal static class RecoveryFingerprint
{
    internal const string Sha256 = "Sha256";
    internal const string XxHash128 = "XxHash128";

    internal static RecoveryVerificationMode Parse(string mode) => mode switch
    {
        Sha256 => RecoveryVerificationMode.Sha256,
        XxHash128 => RecoveryVerificationMode.XxHash128,
        nameof(RecoveryVerificationMode.MetadataOnly) => RecoveryVerificationMode.MetadataOnly,
        nameof(RecoveryVerificationMode.None) => RecoveryVerificationMode.None,
        _ => throw new IOException("Unknown recovery verification mode.")
    };

    internal static bool IsContentVerified(RecoveryVerificationMode mode) =>
        mode is RecoveryVerificationMode.Sha256 or RecoveryVerificationMode.XxHash128;

    internal static bool IsValid(string value, RecoveryVerificationMode mode) => mode switch
    {
        RecoveryVerificationMode.MetadataOnly => value.Length == 32 && value.All(Uri.IsHexDigit),
        RecoveryVerificationMode.None => value == "unverified",
        RecoveryVerificationMode.Sha256 => value.Length == 64 && value.All(Uri.IsHexDigit),
        RecoveryVerificationMode.XxHash128 => value.Length == 32 && value.All(Uri.IsHexDigit),
        _ => false
    };

    internal static string Hash(string path, RecoveryVerificationMode mode = RecoveryVerificationMode.Sha256)
    {
        if (mode == RecoveryVerificationMode.None) return "unverified";
        if (mode == RecoveryVerificationMode.MetadataOnly)
        {
            var info = new FileInfo(path);
            return FormattableString.Invariant($"{info.Length:X16}{info.LastWriteTimeUtc.Ticks:X16}");
        }
        using var input = File.OpenRead(path);
        if (mode == RecoveryVerificationMode.Sha256)
            return Convert.ToHexString(SHA256.HashData(input));
        if (mode != RecoveryVerificationMode.XxHash128) throw new ArgumentOutOfRangeException(nameof(mode));
        var hash = new XxHash128();
        hash.Append(input);
        return Convert.ToHexString(hash.GetCurrentHash());
    }

    internal static string Hash(string path, string mode) => Hash(path, Parse(mode));

    internal static string HashBytes(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    internal static async Task<string> HashFileAsync(string path, RecoveryVerificationMode mode, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!IsContentVerified(mode)) return Hash(path, mode);
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true);
        return await HashStreamAsync(input, mode, token);
    }

    internal static async Task<string> HashStreamAsync(Stream input, RecoveryVerificationMode mode, CancellationToken token)
    {
        if (mode == RecoveryVerificationMode.Sha256)
            return Convert.ToHexString(await SHA256.HashDataAsync(input, token));
        using var hash = CreateContentHash(mode);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(65536);
        try
        {
            int count;
            while ((count = await input.ReadAsync(buffer.AsMemory(), token)) != 0)
                hash.Append(buffer.AsSpan(0, count));
            return hash.GetHex();
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }

    internal static ContentHash CreateContentHash(RecoveryVerificationMode mode)
    {
        if (!IsContentVerified(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        return new ContentHash(mode);
    }

    internal sealed class ContentHash(RecoveryVerificationMode mode) : IDisposable
    {
        private readonly IncrementalHash? sha = mode == RecoveryVerificationMode.Sha256
            ? IncrementalHash.CreateHash(HashAlgorithmName.SHA256) : null;
        private readonly XxHash128? xx = mode == RecoveryVerificationMode.XxHash128 ? new XxHash128() : null;

        internal void Append(ReadOnlySpan<byte> bytes)
        {
            if (sha != null) sha.AppendData(bytes); else xx!.Append(bytes);
        }

        internal string GetHex() => Convert.ToHexString(sha?.GetCurrentHash() ?? xx!.GetCurrentHash());
        public void Dispose() => sha?.Dispose();
    }
}
