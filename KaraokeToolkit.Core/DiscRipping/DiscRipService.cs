using KaraokeToolkit.Core.Execution;
using KaraokeToolkit.Core.Audio;
using KaraokeToolkit.Core.Archives;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Recovery;
using System.Diagnostics;
namespace KaraokeToolkit.Core.DiscRipping;

public sealed record DiscRipRequest(OpticalDisc Disc, int[] Tracks, string Destination, string DiscName, RipFormat Format, ConversionOptions Options);

/// <summary>Reads selected tracks into disposable staging, verifies them, then publishes each track atomically.</summary>
public sealed class DiscRipService(IDiscReader reader)
{
    /// <summary>Calculates a track's destination paths without creating files or reserving their names.</summary>
    public static IReadOnlyList<string> GetOutputPaths(DiscRipRequest request, int track)
    {
        string stem = Path.Combine(Path.GetFullPath(request.Destination), $"{request.DiscName}-{track:00}");
        return request.Format switch
        {
            RipFormat.Bin => [stem + ".bin"],
            RipFormat.Zip => [stem + ".zip"],
            RipFormat.AudioG => [stem + AudioEncoderService.Extension(request.Options.AudioEncoder), stem + ".cdg"],
            _ => throw new ArgumentException("Invalid rip format.")
        };
    }
    /// <summary>Captures, verifies and publishes selected tracks, retaining tracks already completed on cancellation.</summary>
    /// <remarks>Physical capture cannot automatically resume without rechecking the disc.
    /// Multi-file track publication uses the transaction journal and rollback safeguards.</remarks>
    public async Task<ConversionResult> RunAsync(DiscRipRequest request, OverwritePolicy policy,
        IProgress<ConversionProgress>? progress = null, CancellationToken token = default,
        Func<OutputConflict, CancellationToken, Task<ConflictDecision>>? resolve = null)
    {
        ValidateRequest(request, policy);
        var tracks = request.Tracks.Select(n => request.Disc.Tracks.Single(t => t.Number == n)).OrderBy(t => t.Number).ToArray();
        string destination = Path.GetFullPath(request.Destination);
        var results = new List<ConversionFileResult>();
        var watch = Stopwatch.StartNew();
        bool cancelled = false;
        await using var batch = (reader as IDiscBatchReader)?.BeginBatch(request.Disc);
        // Physical-disc reads deliberately cannot auto-resume without inspecting the disc again.
        foreach (var track in tracks)
        {
            string label = $"{request.DiscName}-{track.Number:00}";
            try
            {
                token.ThrowIfCancellationRequested();
                using var transaction = new FileTransaction(destination);
                string bin = Path.Combine(transaction.Workspace, label + ".bin");
                void Report(int percent) => progress?.Report(new(percent, results.Count * 100 / tracks.Length, tracks.Length, results.Count, "Ripping " + label, watch.Elapsed.ToString(@"hh\:mm\:ss")));
                Report(0);
                await reader.ReadTrackAsync(request.Disc, track, bin, new InlineProgress(Report), token);
                token.ThrowIfCancellationRequested();
                BinConverter.Validate(bin);
                if (new FileInfo(bin).Length != track.BinBytes) throw new InvalidDataException("Disc read returned an incomplete track.");
                var outputs = new List<string>();
                if (request.Format == RipFormat.Bin) outputs.Add(bin);
                else
                {
                    string wav = Path.ChangeExtension(bin, ".wav"), cdg = Path.ChangeExtension(bin, ".cdg");
                    await BinConverter.SplitAsync(bin, wav, cdg, token);
                    string audio = Path.ChangeExtension(bin, AudioEncoderService.Extension(request.Options.AudioEncoder));
                    if (request.Options.AudioEncoder != AudioEncoder.WAV)
                        await AudioEncoderService.EncodeAsync(wav, audio, request.Options.AudioEncoder, request.Options.AudioBitrate, token);
                    if (request.Format == RipFormat.AudioG) outputs.AddRange([audio, cdg]);
                    else
                    {
                        string zip = Path.ChangeExtension(bin, ".zip");
                        await KaraokeZipWriter.CreateAsync(audio, cdg, zip, request.Options.ZipCompression, token, request.Options.ZipAlgorithm);
                        KaraokeArchive.VerifyCreated(zip, audio, cdg);
                        outputs.Add(zip);
                    }
                }
                transaction.ObserveTargets(outputs);
                var effective = policy;
                if (effective == OverwritePolicy.Ask)
                {
                    string[] existing = outputs.Select(p => Path.Combine(destination, Path.GetFileName(p))).Where(File.Exists).ToArray();
                    if (existing.Length == 0) effective = OverwritePolicy.Fail;
                    else
                    {
                        if (resolve == null) throw new IOException("Existing output requires an overwrite decision.");
                        effective = await resolve(new(destination, existing), token) switch
                        {
                            ConflictDecision.Skip => OverwritePolicy.Skip,
                            ConflictDecision.Replace => OverwritePolicy.Replace,
                            _ => throw new OperationCanceledException(token)
                        };
                    }
                }
                var committed = transaction.Commit(outputs, [], effective, token);
                results.Add(new(label, ConversionStatus.Succeeded, committed, "Track read, verified and published."));
                Report(100);
            }
            catch (OperationCanceledException) { results.Add(new(label, ConversionStatus.Cancelled, [], "Cancelled; incomplete track discarded.")); cancelled = true; break; }
            catch (OutputSkippedException) { results.Add(new(label, ConversionStatus.Skipped, [], "Existing output retained.")); }
            catch (Exception error) { results.Add(new(label, ConversionStatus.Failed, [], error.Message)); }
        }
        // Match conversion metadata preferences and conflict handling after publishing tracks.
        if (request.Options.AutoCreateCueText && !cancelled && results.Any(r => r.Status == ConversionStatus.Succeeded))
        {
            string extension = request.Format switch { RipFormat.Bin => ".bin", RipFormat.Zip => ".zip", _ => ".cdg" };
            try
            {
                var metadata = await ConversionService.WriteMetadataAsync(destination, policy, token, resolve, extension);
                int first = results.FindIndex(r => r.Status == ConversionStatus.Succeeded);
                results[first] = results[first] with { Outputs = results[first].Outputs.Concat(metadata.Outputs).ToArray() };
            }
            catch (OperationCanceledException) { cancelled = true; }
            catch (OutputSkippedException) { results.Add(new(destination, ConversionStatus.Skipped, [], "Existing CUE/TXT retained.") { IsMetadata = true }); }
            catch (Exception error) { results.Add(new(destination, ConversionStatus.Failed, [], "CUE/TXT: " + error.Message) { IsMetadata = true }); }
        }
        return new(results, cancelled) { Found = tracks.Length, Elapsed = watch.Elapsed };
    }
    /// <summary>Preflight shared by execution and read-only CLI planning.</summary>
    public static void ValidateRequest(DiscRipRequest request, OverwritePolicy policy)
    {
        if (!request.Options.ExperimentalDiscRipping) throw new InvalidOperationException("Enable experimental CD+G ripping in Options first.");
        if (!request.Disc.SupportsCdg) throw new NotSupportedException("This drive does not report CD+G reading support.");
        if (!Enum.IsDefined(request.Format) || !Enum.IsDefined(policy)) throw new ArgumentException("Invalid ripping option.");
        if (string.IsNullOrWhiteSpace(request.DiscName) || request.DiscName is "." or ".." || request.DiscName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || request.DiscName.Contains('/') || request.DiscName.Contains('\\') || request.DiscName.EndsWith('.') || request.DiscName.EndsWith(' '))
            throw new ArgumentException("Enter a valid disc name without path separators.");
        if (request.Tracks.Length == 0 || request.Tracks.Distinct().Count() != request.Tracks.Length) throw new ArgumentException("Select at least one track, without duplicates.");
        var tracks = request.Tracks.Select(n => request.Disc.Tracks.SingleOrDefault(t => t.Number == n) ?? throw new ArgumentException("Selected track is not on this disc.")).OrderBy(t => t.Number).ToArray();
        if (tracks.Any(t => t.AudioBytes <= 0 || t.AudioBytes % 2352 != 0)) throw new InvalidDataException("Invalid audio track length.");
        new ConversionRequest(OperationId.BinToZip, request.Destination, request.Options).Validate();
        if (request.Format != RipFormat.Bin)
            AudioEncoderService.Validate(request.Options.AudioEncoder, request.Options.AudioBitrate);
        string destination = Path.GetFullPath(request.Destination);
        if (!Directory.Exists(destination)) throw new DirectoryNotFoundException("Choose an existing output folder.");
        RunSession.CheckPlainPath(destination);
    }
    private sealed class InlineProgress(System.Action<int> report) : IProgress<int> { public void Report(int value) => report(value); }
}
