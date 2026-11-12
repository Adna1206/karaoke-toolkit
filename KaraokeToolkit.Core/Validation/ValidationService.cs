using KaraokeToolkit.Core.Archives;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Recovery;
using SharpCompress.Archives.Zip;
using System.Security.Cryptography;

namespace KaraokeToolkit.Core.Validation;

public enum ValidationStatus
{
    Valid, Incomplete, Ambiguous, Damaged, Unsupported, Unreadable
}
public sealed record ValidatedFile(string Path, string Sha256);
public sealed record ValidationFinding(string Source, string Root, ValidationStatus Status, string Message, IReadOnlyList<ValidatedFile> Files)
{
    public bool CanMove => Files.Count > 0 && Status is ValidationStatus.Incomplete or ValidationStatus.Ambiguous or ValidationStatus.Damaged or ValidationStatus.Unsupported;
}
public sealed record ValidationResult(IReadOnlyList<ValidationFinding> Findings, bool Cancelled, TimeSpan Elapsed);

/// <summary>Read-only structural validation. A valid result does not certify decoded audio or visual CDG content.</summary>
public sealed class ValidationService
{
    public Task<ValidationResult> ScanAsync(IEnumerable<string> inputs, bool recurse, IProgress<ConversionProgress>? progress = null, CancellationToken token = default)
        => Task.Run(async () =>
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var media = InputSelection.Media(inputs, recurse).ToArray();
            var visited = new HashSet<string>(InputSelection.Paths);
            var findings = new List<ValidationFinding>();
            bool cancelled = false;
            foreach (var (file, root) in media)
            {
                if (token.IsCancellationRequested)
                {
                    cancelled = true;
                    break;
                }
                if (!visited.Add(file))
                    continue;
                string[] group = [file];
                ValidatedFile[]? hashes = null;
                try
                {
                    string extension = Path.GetExtension(file).ToLowerInvariant();
                    if (extension is not (".bin" or ".zip") && !ArchiveZipConverter.IsInput(file))
                    {
                        group = Directory.GetFiles(Path.GetDirectoryName(file)!).Where(p =>
                            Path.GetFileNameWithoutExtension(p).Equals(Path.GetFileNameWithoutExtension(file), StringComparison.OrdinalIgnoreCase) &&
                            (Path.GetExtension(p).Equals(".cdg", StringComparison.OrdinalIgnoreCase) || KaraokeArchive.AudioExtensions.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase))).Order(InputSelection.Paths).ToArray();
                        foreach (string member in group)
                            visited.Add(member);
                    }
                    hashes = await FingerprintAsync(group, token);
                    ValidationStatus status = ValidationStatus.Valid;
                    string message;
                    if (ArchiveZipConverter.IsInput(file))
                    {
                        status = ValidationStatus.Unsupported;
                        message = "Use Convert Archive to ZIP for this format. Karaoke validation supports ZIP and loose karaoke files.";
                    }
                    else if (extension == ".bin")
                    {
                        BinConverter.Validate(file);
                        message = "BIN sector structure is valid. Audio and graphics rendering were not tested.";
                    }
                    else if (extension == ".zip")
                    {
                        string? warning = await CheckArchiveAsync(file, token);
                        status = warning == null ? ValidationStatus.Valid : ValidationStatus.Unsupported;
                        message = warning ?? "ZIP CRCs and audio/CDG pair structure are valid. Audio decoding was not tested.";
                    }
                    else
                    {
                        string[] graphics = group.Where(p => Path.GetExtension(p).Equals(".cdg", StringComparison.OrdinalIgnoreCase)).ToArray();
                        string[] audio = group.Except(graphics, InputSelection.Paths).ToArray();
                        foreach (string cdg in graphics)
                            KaraokeArchive.ValidateGraphics(cdg);
                        if (audio.Any(p => new FileInfo(p).Length == 0))
                            throw new InvalidDataException("Audio file is empty.");
                        if (graphics.Length == 0 || audio.Length == 0)
                        {
                            status = ValidationStatus.Incomplete;
                            message = graphics.Length == 0 ? "Missing matching CDG file. The audio may still be usable." : "Missing matching audio file. The CDG may still be usable.";
                        }
                        else if (graphics.Length != 1 || audio.Length != 1)
                        {
                            status = ValidationStatus.Ambiguous;
                            message = "Multiple matching audio/CDG files. Choose the intended pair before converting.";
                        }
                        else
                            message = "Matching pair and CDG packet structure are valid. Audio decoding was not tested.";
                    }
                    var after = await FingerprintAsync(group, token);
                    if (!hashes.SequenceEqual(after))
                        throw new IOException("Files changed during validation. Scan again.");
                    findings.Add(new(file, root, status, message, hashes));
                }
                catch (OperationCanceledException) { cancelled = true; break; }
                catch (Exception error) when (error is InvalidDataException or SharpCompress.Common.ArchiveException)
                {
                    try
                    {
                        var after = await FingerprintAsync(group, token);
                        if (hashes == null || !hashes.SequenceEqual(after))
                            throw new IOException("Files changed during validation. Scan again.");
                        findings.Add(new(file, root, ValidationStatus.Damaged, error.Message, after));
                    }
                    catch (OperationCanceledException) { cancelled = true; break; }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException) { findings.Add(new(file, root, ValidationStatus.Unreadable, e.Message, [])); }
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    findings.Add(new(file, root, ValidationStatus.Unreadable, error.Message, []));
                }
                catch (NotSupportedException error)
                {
                    findings.Add(new(file, root, ValidationStatus.Unsupported, error.Message, []));
                }
                progress?.Report(new(100, media.Length == 0 ? 100 : Math.Min(100, visited.Count * 100 / media.Length), media.Length, findings.Count, "Checked: " + file, watch.Elapsed.ToString(@"hh\:mm\:ss")));
            }
            return new ValidationResult(findings, cancelled, watch.Elapsed);
        }, CancellationToken.None);

    internal static async Task<ValidatedFile[]> FingerprintAsync(IEnumerable<string> files, CancellationToken token)
    {
        var result = new List<ValidatedFile>();
        foreach (string path in files)
        {
            RunSession.CheckPlainPath(path);
            await using var stream = File.OpenRead(path);
            result.Add(new(path, Convert.ToHexString(await SHA256.HashDataAsync(stream, token))));
        }
        return result.ToArray();
    }

    private static async Task<string?> CheckArchiveAsync(string path, CancellationToken token)
    {
        using var zip = ZipArchive.OpenArchive(path);
        string? warning = null;
        var files = zip.Entries.Where(e => !e.IsDirectory).ToArray();
        foreach (var entry in zip.Entries)
        {
            string name = entry.Key!.Replace('\\', '/');
            if (name.StartsWith('/') || name.Contains(':') || name.Split('/').Contains(".."))
                warning = "ZIP contains unsafe entry paths; it cannot be converted safely.";
        }
        foreach (var entry in files)
        {
            token.ThrowIfCancellationRequested();
            if (entry.Size > 4L * 1024 * 1024 * 1024)
                return "ZIP contains a member exceeding the supported 4 GiB limit.";
            if (entry.Size == 0 && !InputSelection.Extensions.Contains(Path.GetExtension(entry.Key!), StringComparer.OrdinalIgnoreCase))
                continue;
            await KaraokeArchive.ReadEntryAsync(entry, Stream.Null, token);
            if (Path.GetExtension(entry.Key!).Equals(".cdg", StringComparison.OrdinalIgnoreCase) && entry.Size % 24 != 0)
                throw new InvalidDataException("ZIP contains an incomplete CDG packet.");
        }
        var graphics = files.Where(e => Path.GetExtension(e.Key!).Equals(".cdg", StringComparison.OrdinalIgnoreCase)).ToArray();
        var audio = files.Where(e => KaraokeArchive.AudioExtensions.Contains(Path.GetExtension(e.Key!), StringComparer.OrdinalIgnoreCase)).ToArray();
        if (files.Length != 2 || graphics.Length != 1 || audio.Length != 1 ||
            !Path.GetFileNameWithoutExtension(graphics[0].Key!).Equals(Path.GetFileNameWithoutExtension(audio[0].Key!), StringComparison.OrdinalIgnoreCase))
            warning ??= "ZIP is readable but does not contain exactly one matching audio/CDG pair. It may contain additional usable files.";
        return warning;
    }
}
