using KaraokeToolkit.Core.Video;
using KaraokeToolkit.Core.Audio;
using KaraokeToolkit.Core.Archives;
using KaraokeToolkit.Core.FileOperations;
using KaraokeToolkit.Core.Recovery;
using KaraokeToolkit.Core.Validation;
using System.Diagnostics;
using Action = KaraokeToolkit.Core.Conversion.OperationCatalog.defAction;
namespace KaraokeToolkit.Core.Conversion;

/// <summary>Local conversion workflow with verified staging, per-source results and safe cancellation.</summary>
/// <remarks>Converters only see disposable inputs. Source deletion occurs after all outputs
/// for that source group have been published and verified. Previously completed groups remain on cancellation.</remarks>
public sealed class ConversionService
{
    private readonly RecoveryStore recovery;
    public ConversionService(RecoveryStore? recovery = null) => this.recovery = recovery ?? new();

    public Task<ConversionResult> ResumeAsync(RecoveryJob job, IProgress<ConversionProgress>? progress = null,
        CancellationToken cancellationToken = default,
        Func<OutputConflict, CancellationToken, Task<ConflictDecision>>? resolveConflict = null)
        => Task.Run(async () =>
        {
            using var run = new RunSession(recovery, job.JournalPath);
            if (run.Data.Finished)
                throw new IOException("This run has already finished.");
            cancellationToken.ThrowIfCancellationRequested();
            run.RecoverPending();
            return await RunCoreAsync(run.Data.Request, run.Data.Policy, progress, cancellationToken, resolveConflict, run, run.Data.ValidateBeforeConversion);
        }, CancellationToken.None);

    public Task<ConversionResult> RunAsync(OperationRequest request,
        OverwritePolicy overwrite = OverwritePolicy.Fail, IProgress<ConversionProgress>? progress = null,
        CancellationToken cancellationToken = default,
        Func<OutputConflict, CancellationToken, Task<ConflictDecision>>? resolveConflict = null, bool validateBeforeConversion = false)
        => Task.Run(() => RunCoreAsync(request, overwrite, progress, cancellationToken, resolveConflict, validateBeforeConversion: validateBeforeConversion), CancellationToken.None);

    private async Task<ConversionResult> RunCoreAsync(OperationRequest request, OverwritePolicy overwrite,
        IProgress<ConversionProgress>? progress, CancellationToken token,
        Func<OutputConflict, CancellationToken, Task<ConflictDecision>>? resolveConflict, RunSession? resumed = null, bool validateBeforeConversion = false)
    {
        request.Validate();
        if (!Enum.IsDefined(overwrite))
            throw new ArgumentOutOfRangeException(nameof(overwrite));
        if (!Directory.Exists(request.Folder))
            throw new DirectoryNotFoundException("Choose an existing source folder.");
        request = request with
        {
            Folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.Folder))
        };
        if (OperationCapabilities.Describe(request.Action) != OperationSupport.Supported)
            throw new NotSupportedException("This action is not supported by the local conversion workflow.");
        if (request.Action is Action.Bin_To_AudioG or Action.Bin_To_Zip or Action.LosslessG_To_Mp3G)
        {
            // This local workflow never invokes deferred online providers. Legacy preferences are retained only for compatibility.
            if (request.Action == Action.LosslessG_To_Mp3G)
            {
                if (!AudioEncoderService.SupportedBitrates.Contains(request.Options.AudioBitrate))
                    throw new ArgumentOutOfRangeException("AudioBitrate");
            }
            else AudioEncoderService.Validate(request.Options.AudioEncoder, request.Options.AudioBitrate);
        }
        token.ThrowIfCancellationRequested();
        string[] folders = EnumerateFolders(Path.GetFullPath(request.Folder), request.Options.RecurseFolders).ToArray();
        bool maintenance = request.Action is Action.Cleanup_Filename or Action.Cleanup_Filenames or Action.Create_Cue_And_Text_Files;
        string extension = request.Action switch
        {
            Action.AudioG_To_Bin or Action.AudioG_To_Zip or Action.LosslessG_To_Mp3G => ".cdg",
            Action.Repack_Zip or Action.Zip_To_Bin or Action.Zip_To_AudioG => ".zip",
            _ => ".bin"
        };
        string[] sources = resumed?.Data.Sources ?? (maintenance ? folders : folders.SelectMany(Directory.GetFiles).Where(p =>
            request.Action == Action.Karaoke_To_Video ? IsVideoSource(p) :
            request.Action == Action.Archive_To_Zip ? ArchiveZipConverter.IsInput(p) :
            Path.GetExtension(p).Equals(extension, StringComparison.OrdinalIgnoreCase)).Order(StringComparer.OrdinalIgnoreCase).ToArray());
        using var fresh = resumed == null ? new RunSession(recovery, request, overwrite, sources, validateBeforeConversion) : null;
        var run = resumed ?? fresh!;
        RunSession.Current.Value = run;
        var results = run.Data.Results.Values.Concat(run.Data.MetadataResults.Values.Where(r => r.Status is ConversionStatus.Failed or ConversionStatus.Skipped)).ToList();
        var watch = Stopwatch.StartNew();
        bool cancelled = false;
        void Report(int percent, string message) => progress?.Report(new(percent, sources.Length == 0 ? 100 : Math.Min(100, results.Count * 100 / sources.Length), sources.Length, results.Count, message, watch.Elapsed.ToString(@"hh\:mm\:ss")));
        Report(0, $"Found {sources.Length} source group(s).");
        foreach (string source in sources)
        {
            if (run.Data.Results.ContainsKey(source))
                continue;
            if (token.IsCancellationRequested)
            {
                cancelled = true;
                break;
            }
            try
            {
                run.Begin(source);
                Report(0, "Processing: " + source);
                if (validateBeforeConversion && !maintenance && request.Action is not (Action.Archive_To_Zip or Action.Repack_Zip))
                {
                    Report(0, "Validating input: " + source);
                    var validation = await new ValidationService().ScanAsync([source], false, token: token);
                    token.ThrowIfCancellationRequested();
                    var issue = validation.Findings.FirstOrDefault(f => f.Status != ValidationStatus.Valid);
                    if (issue != null)
                        throw new InvalidDataException($"Input validation ({issue.Status}): {issue.Message}");
                    if (validation.Findings.Count == 0)
                        throw new InvalidDataException("Input validation found no source group.");
                }
                ConversionFileResult result;
                if (request.Action is Action.Cleanup_Filename or Action.Cleanup_Filenames)
                    result = await FilenameCleanup.RunAsync(source, token);
                else if (request.Action == Action.Create_Cue_And_Text_Files)
                    result = await WriteMetadataAsync(source, overwrite, token, resolveConflict);
                else
                    result = await ConvertOneAsync(source, request, overwrite, token, Report, resolveConflict);
                run.Complete(result);
                results.Add(result);
                Report(100, result.Status + ": " + source + " — " + result.Message);
            }
            catch (OperationCanceledException)
            {
                results.Add(new(source, ConversionStatus.Cancelled, [], "Cancelled before publication."));
                cancelled = true;
                break;
            }
            catch (OutputSkippedException) { var result = new ConversionFileResult(source, ConversionStatus.Skipped, [], "Existing output retained."); run.Complete(result); results.Add(result); Report(100, "Skipped: " + source); }
            catch (Exception error)
            {
                run.ThrowIfUnresolved();
                var result = new ConversionFileResult(source, ConversionStatus.Failed, [], error.Message);
                run.Complete(result);
                results.Add(result);
                Report(100, "Failed: " + source + " — " + error.Message);
            }
        }
        if (!maintenance && request.Action is not (Action.Archive_To_Zip or Action.Repack_Zip or Action.Karaoke_To_Video) && request.Options.AutoCreateCueText && !cancelled)
        {
            string listingExtension = request.Action switch
            {
                Action.AudioG_To_Bin or Action.Zip_To_Bin => ".bin",
                Action.AudioG_To_Zip or Action.Bin_To_Zip => ".zip",
                _ => ".cdg"
            };
            foreach (string folder in results.Where(r => r.Status == ConversionStatus.Succeeded).Select(r => Path.GetDirectoryName(r.Source)!).Distinct().ToArray())
            {
                if (run.Data.Metadata.Contains(folder))
                    continue;
                run.Begin(folder, metadata: true);
                try
                {
                    var metadata = await WriteMetadataAsync(folder, overwrite, token, resolveConflict, listingExtension);
                    run.Complete(metadata);
                }
                catch (OperationCanceledException) { cancelled = true; break; }
                catch (OutputSkippedException) { var result = new ConversionFileResult(folder, ConversionStatus.Skipped, [], "Existing CUE/text retained."); run.Complete(result); results.Add(result); }
                catch (Exception error) { run.ThrowIfUnresolved(); var result = new ConversionFileResult(folder, ConversionStatus.Failed, [], "CUE/text: " + error.Message) { IsMetadata = true }; run.Complete(result); results.Add(result); }
            }
        }
        run.ThrowIfUnresolved();
        run.Finish();
        RunSession.Current.Value = null;
        return new(results, cancelled || token.IsCancellationRequested)
        {
            Found = sources.Length,
            Elapsed = watch.Elapsed,
            RetryRequests = results.Where(r => r.Status == ConversionStatus.Failed)
                .GroupBy(r => r.IsMetadata ? Action.Create_Cue_And_Text_Files : request.Action)
                .Select(g => new ConversionRetry(g.Key, g.Select(r => r.Source).Distinct(InputSelection.Paths).ToArray(),
                    request.Options with
                    {
                        RecurseFolders = false
                    }, overwrite)
                {
                    ValidateBeforeConversion = validateBeforeConversion
                }).ToArray()
        };
    }

    internal static IEnumerable<string> EnumerateFolders(string root, bool recurse)
    {
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Linked source folders are not supported.");
        yield return root;
        if (!recurse)
            yield break;
        foreach (string child in Directory.GetDirectories(root).Order(StringComparer.OrdinalIgnoreCase))
        {
            if (InputSelection.IsExcludedFolder(Path.GetFileName(child)) || (File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
                continue;
            foreach (string nested in EnumerateFolders(child, true))
                yield return nested;
        }
    }
    internal static bool IsVideoSource(string path) => Path.GetExtension(path).ToLowerInvariant() is ".bin" or ".zip" or ".cdg";
    internal static async Task<ConversionFileResult> WriteMetadataAsync(string folder, OverwritePolicy overwrite, CancellationToken token,
        Func<OutputConflict, CancellationToken, Task<ConflictDecision>>? resolveConflict, string? extension = null)
    {
        using var transaction = new FileTransaction(folder);
        var outputs = CueTextService.Write(folder, transaction.Workspace, extension);
        if (outputs.Count == 0)
            return new(folder, ConversionStatus.Skipped, [], "No tracks for CUE/text output.");
        transaction.ObserveTargets(outputs);
        overwrite = await ResolveOverwriteAsync(folder, outputs, overwrite, resolveConflict, token);
        var committed = transaction.Commit(outputs, [], overwrite, token);
        return new(folder, ConversionStatus.Succeeded, committed, "Created CUE/text files.");
    }
    private static async Task<ConversionFileResult> ConvertOneAsync(string source, OperationRequest request, OverwritePolicy overwrite, CancellationToken token, System.Action<int, string> report,
        Func<OutputConflict, CancellationToken, Task<ConflictDecision>>? resolveConflict)
    {
        using var transaction = new FileTransaction(Path.GetDirectoryName(source)!);
        string staged = await transaction.CopyInputAsync(source, token);
        var inputs = new List<string> { source };
        var outputs = new List<string>();
        string audio = "", graphics = "";
        if (request.Action is Action.AudioG_To_Bin or Action.AudioG_To_Zip)
        {
            graphics = staged;
            KaraokeArchive.ValidateGraphics(graphics);
            string stem = Path.GetFileNameWithoutExtension(source);
            string[] matches = Directory.GetFiles(Path.GetDirectoryName(source)!).Where(p => Path.GetFileNameWithoutExtension(p).Equals(stem, StringComparison.OrdinalIgnoreCase) && KaraokeArchive.AudioExtensions.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase)).ToArray();
            if (matches.Length != 1)
                throw new InvalidDataException("Expected exactly one matching audio file.");
            inputs.Add(matches[0]);
            audio = await transaction.CopyInputAsync(matches[0], token);
        }
        if (request.Action == Action.LosslessG_To_Mp3G)
        {
            graphics = staged;
            KaraokeArchive.ValidateGraphics(graphics);
            string stem = Path.GetFileNameWithoutExtension(source);
            string[] matches = Directory.GetFiles(Path.GetDirectoryName(source)!).Where(p =>
                Path.GetFileNameWithoutExtension(p).Equals(stem, StringComparison.OrdinalIgnoreCase) &&
                LosslessAudioTranscoder.IsCandidate(p)).ToArray();
            if (matches.Length != 1)
                throw new InvalidDataException("Expected exactly one matching lossless audio file.");
            string stagedAudio = await transaction.CopyInputAsync(matches[0], token);
            inputs.Add(matches[0]);
            string mp3 = Path.ChangeExtension(staged, ".mp3");
            report(35, "Encoding MP3: " + source);
            await LosslessAudioTranscoder.EncodeAsync(stagedAudio, mp3, request.Options.AudioBitrate, token);
            outputs.Add(mp3);
        }
        if (request.Action is Action.Zip_To_AudioG or Action.Zip_To_Bin)
        {
            (audio, graphics) = await KaraokeArchive.ExtractAsync(staged, token);
            if (request.Action == Action.Zip_To_AudioG)
                outputs.AddRange([audio, graphics]);
        }
        if (request.Action is Action.Bin_To_AudioG or Action.Bin_To_Zip)
        {
            graphics = Path.ChangeExtension(staged, ".cdg");
            string wav = Path.ChangeExtension(staged, ".wav");
            await BinConverter.SplitAsync(staged, wav, graphics, token);
            audio = Path.ChangeExtension(staged, AudioEncoderService.Extension(request.Options.AudioEncoder));
            report(35, "Encoding: " + source);
            if (request.Options.AudioEncoder != AudioEncoder.WAV)
                await AudioEncoderService.EncodeAsync(wav, audio, request.Options.AudioEncoder, request.Options.AudioBitrate, token);
            if (request.Action == Action.Bin_To_AudioG)
                outputs.AddRange([audio, graphics]);
        }
        if (request.Action is Action.AudioG_To_Bin or Action.Zip_To_Bin)
        {
            string pcm = Path.Combine(transaction.Workspace, "decoded.pcm");
            report(25, "Decoding: " + source);
            await AudioDecoderService.DecodeAsync(audio, pcm, token);
            string bin = Path.ChangeExtension(graphics, ".bin");
            await BinConverter.CombineAsync(pcm, graphics, bin, token);
            BinConverter.Validate(bin);
            outputs.Add(bin);
        }
        if (request.Action is Action.AudioG_To_Zip or Action.Bin_To_Zip)
        {
            string zip = Path.ChangeExtension(staged, ".zip");
            report(60, "Creating ZIP: " + source);
            await KaraokeZipWriter.CreateAsync(audio, graphics, zip, request.Options.ZipCompression, token, request.Options.ZipAlgorithm);
            KaraokeArchive.VerifyCreated(zip, audio, graphics);
            outputs.Add(zip);
        }
        if (request.Action == Action.Archive_To_Zip)
        {
            string zip = Path.Combine(transaction.Workspace, ArchiveZipConverter.OutputName(source));
            report(20, "Converting archive: " + source);
            await ArchiveZipConverter.ConvertAsync(staged, zip, request.Options, token);
            outputs.Add(zip);
        }
        if (request.Action == Action.Repack_Zip)
        {
            string zip = Path.Combine(transaction.Workspace, Path.GetFileNameWithoutExtension(source) + " (repacked).zip");
            report(20, "Repacking ZIP: " + source);
            await ArchiveZipConverter.ConvertAsync(staged, zip, request.Options, token, repackZip: true);
            if (request.Options.DeleteOldFiles)
            {
                // Replace only the staged input here. Commit backs up the real source
                // and publishes the verified ZIP under its original name with rollback.
                File.Delete(staged);
                File.Move(zip, staged);
                zip = staged;
            }
            outputs.Add(zip);
        }
        if (request.Action == Action.Karaoke_To_Video)
        {
            string kind = Path.GetExtension(source).ToLowerInvariant();
            if (kind == ".bin")
            {
                graphics = Path.ChangeExtension(staged, ".cdg");
                audio = Path.ChangeExtension(staged, ".wav");
                await BinConverter.SplitAsync(staged, audio, graphics, token);
            }
            else if (kind == ".zip")
                (audio, graphics) = await KaraokeArchive.ExtractAsync(staged, token);
            else if (kind == ".cdg")
            {
                graphics = staged;
                string stem = Path.GetFileNameWithoutExtension(source);
                string[] matches = Directory.GetFiles(Path.GetDirectoryName(source)!).Where(p =>
                    Path.GetFileNameWithoutExtension(p).Equals(stem, StringComparison.OrdinalIgnoreCase) &&
                    KaraokeArchive.AudioExtensions.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase)).ToArray();
                if (matches.Length != 1) throw new InvalidDataException("Expected exactly one matching audio file for CDG video conversion.");
                inputs.Add(matches[0]);
                audio = await transaction.CopyInputAsync(matches[0], token);
            }
            else throw new ArgumentException("Video conversion requires BIN, ZIP or matching Audio+G input.");
            string video = Path.ChangeExtension(staged, VideoOptions.Extension(request.Options.VideoContainer));
            report(35, "Rendering CDG video: " + source);
            await VideoEncoderService.EncodeAsync(graphics, audio, video, request.Options, token);
            outputs.Add(video);
        }
        token.ThrowIfCancellationRequested();
        report(90, "Verified outputs: " + source);
        token.ThrowIfCancellationRequested();
        transaction.ObserveTargets(outputs);
        // Repacking in place intentionally replaces its own input, not an unrelated
        // existing output. Source fingerprints still guard against concurrent changes.
        overwrite = request.Action == Action.Repack_Zip && request.Options.DeleteOldFiles
            ? OverwritePolicy.Fail
            : await ResolveOverwriteAsync(Path.GetDirectoryName(source)!, outputs, overwrite, resolveConflict, token);
        // The CDG remains the graphics half of MP3+G; only the old lossless audio may be removed.
        var retired = request.Options.DeleteOldFiles && request.Action != Action.Karaoke_To_Video
            ? request.Action == Action.LosslessG_To_Mp3G ? inputs.Skip(1).ToArray() : inputs.ToArray()
            : [];
        var committed = transaction.Commit(outputs, retired, overwrite, token);
        return new(source, ConversionStatus.Succeeded, committed, "Outputs verified and published.");
    }
    private static async Task<OverwritePolicy> ResolveOverwriteAsync(string folder, IReadOnlyList<string> outputs,
        OverwritePolicy policy, Func<OutputConflict, CancellationToken, Task<ConflictDecision>>? resolve,
        CancellationToken token)
    {
        if (policy != OverwritePolicy.Ask)
            return policy;
        string[] existing = outputs.Select(p => Path.Combine(folder, Path.GetFileName(p))).Where(File.Exists).ToArray();
        if (existing.Length == 0)
            return OverwritePolicy.Fail; // Refuse a new collision appearing after this check.
        if (resolve == null)
            throw new IOException("Output exists. Choose Skip or Replace under Options → General → Existing output files.");
        token.ThrowIfCancellationRequested();
        var decision = await resolve(new(folder, existing), token);
        token.ThrowIfCancellationRequested();
        return decision switch
        {
            ConflictDecision.Skip => OverwritePolicy.Skip,
            ConflictDecision.Replace => OverwritePolicy.Replace,
            ConflictDecision.Cancel => throw new OperationCanceledException("Cancelled at the overwrite prompt."),
            _ => throw new InvalidOperationException("Invalid overwrite decision.")
        };
    }
}





