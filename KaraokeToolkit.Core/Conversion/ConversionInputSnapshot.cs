using KaraokeToolkit.Core.Archives;
using KaraokeToolkit.Core.Audio;
using KaraokeToolkit.Core.Execution;
using KaraokeToolkit.Core.Recovery;
using System.Text.Json.Serialization;

namespace KaraokeToolkit.Core.Conversion;

internal sealed record ConversionInputFingerprint(string Path, string? Hash)
{
    // Earlier conversion baselines stored the digest under this name.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Sha256 { get; init; }
    [JsonIgnore]
    internal string? Value => Hash ?? Sha256;
}

/// <summary>The input paths and selected fingerprints for one conversion group at batch preparation.</summary>
internal sealed record ConversionInputSnapshot(ConversionInputFingerprint[] Files, string? Error = null)
{
    internal static async Task<Dictionary<string, ConversionInputSnapshot>> CaptureAsync(
        OperationId operation, IEnumerable<string> sources, CancellationToken token,
        RecoveryVerificationMode verification = RecoveryVerificationMode.Sha256)
    {
        var snapshots = new Dictionary<string, ConversionInputSnapshot>(InputSelection.Paths);
        foreach (string source in sources)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                string? audio = FindAudioCompanion(operation, source);
                string[] paths = audio == null ? [source] : [source, audio];
                var files = new ConversionInputFingerprint[paths.Length];
                for (int i = 0; i < paths.Length; i++)
                {
                    string path = paths[i];
                    if (RunSession.CheckPlainPath(path) is not { } attributes || attributes.HasFlag(FileAttributes.Directory))
                        throw new IOException("Conversion input is missing or is a directory: " + path);
                    files[i] = new(path, await RecoveryFingerprint.HashFileAsync(path, verification, token));
                }
                snapshots.Add(source, new(files));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                // An unreadable or incomplete group should fail independently of the rest of the batch.
                snapshots.Add(source, new([], error.Message));
            }
        }
        return snapshots;
    }

    internal static string? FindAudioCompanion(OperationId operation, string source)
    {
        string? missing = operation switch
        {
            OperationId.AudioGToBin or OperationId.AudioGToZip => "Expected exactly one matching audio file.",
            OperationId.LosslessGToMp3G => "Expected exactly one matching lossless audio file.",
            OperationId.KaraokeToVideo when Path.GetExtension(source).Equals(".cdg", StringComparison.OrdinalIgnoreCase)
                => "Expected exactly one matching audio file for CDG video conversion.",
            _ => null
        };
        if (missing == null) return null;

        string stem = Path.GetFileNameWithoutExtension(source);
        string[] matches = Directory.GetFiles(Path.GetDirectoryName(source)!).Where(path =>
            Path.GetFileNameWithoutExtension(path).Equals(stem, StringComparison.OrdinalIgnoreCase) &&
            (operation == OperationId.LosslessGToMp3G ? LosslessAudioTranscoder.IsCandidate(path) :
                KaraokeArchive.AudioExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))).ToArray();
        if (matches.Length != 1) throw new InvalidDataException(missing);
        return matches[0];
    }

    internal string FingerprintFor(string path) =>
        Files.SingleOrDefault(file => InputSelection.Paths.Equals(file.Path, path))?.Value
        ?? throw new IOException("Conversion input group changed since the batch started: " + path);
}
