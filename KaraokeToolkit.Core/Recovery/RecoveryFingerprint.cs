namespace KaraokeToolkit.Core.Recovery;

public enum RecoveryVerificationMode
{
    Sha256,
    XxHash128,
    MetadataOnly,
    None
}

internal static class RecoveryFingerprint
{
    internal const string Sha256 = "Sha256";
    internal const string XxHash128 = "XxHash128";

    internal static int HexLength(string mode) => mode switch
    {
        Sha256 => 64,
        XxHash128 => 32,
        _ => throw new IOException("Unknown recovery verification mode.")
    };

    internal static bool IsContentVerified(RecoveryVerificationMode mode) =>
        mode is RecoveryVerificationMode.Sha256 or RecoveryVerificationMode.XxHash128;

    internal static bool IsValid(string value, RecoveryVerificationMode mode) => mode switch
    {
        RecoveryVerificationMode.MetadataOnly => value.Length == 32 && value.All(Uri.IsHexDigit),
        RecoveryVerificationMode.None => value == "unverified",
        _ => value.Length == HexLength(mode.ToString()) && value.All(Uri.IsHexDigit)
    };

    internal static string Hash(string path, RecoveryVerificationMode mode)
    {
        if (mode == RecoveryVerificationMode.None) return "unverified";
        if (mode == RecoveryVerificationMode.MetadataOnly)
        {
            var info = new FileInfo(path);
            return FormattableString.Invariant($"{info.Length:X16}{info.LastWriteTimeUtc.Ticks:X16}");
        }
        return Hash(path, mode.ToString());
    }

    internal static string Hash(string path, string mode)
    {
        if (mode == Sha256) return FileTransaction.Hash(path);
        if (mode != XxHash128) throw new IOException("Unknown recovery verification mode.");
        using var input = File.OpenRead(path);
        var digest = new System.IO.Hashing.XxHash128();
        digest.Append(input);
        return Convert.ToHexString(digest.GetCurrentHash());
    }
}
