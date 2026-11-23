using KaraokeToolkit.Core.Execution;
using KaraokeToolkit.Core.Video;
using KaraokeToolkit.Core.Audio;
using KaraokeToolkit.Core.Archives;
using KaraokeToolkit.Core.FileOperations;
using KaraokeToolkit.Core.Recovery;
using KaraokeToolkit.Core.Validation;
using System.Diagnostics;
using Action = KaraokeToolkit.Core.Execution.OperationId;
namespace KaraokeToolkit.Core.Conversion;

/// <summary>Local conversion workflow with verified staging, per-source results and safe cancellation.</summary>
/// <remarks>Converters only see disposable inputs. Source deletion occurs after all outputs
/// for that source group have been published and verified. Previously completed groups remain on cancellation.</remarks>
public sealed partial class ConversionService
{
    private readonly RecoveryStore recovery;
    public ConversionService(RecoveryStore? recovery = null) => this.recovery = recovery ?? new();

    /// <summary>Resolves pending publication and continues a conversion using its saved request and source plan.</summary>
    /// <remarks>Completed groups remain committed. Recovery validation failures propagate to the caller.</remarks>
    public Task<ConversionResult> ResumeAsync(RecoveryJob job, IProgress<ConversionProgress>? progress = null,
        CancellationToken cancellationToken = default,
        Func<OutputConflict, CancellationToken, Task<ConflictDecision>>? resolveConflict = null)
        => ResumePassAsync(job, progress, cancellationToken, resolveConflict, null, freshSelection: false);

    internal Task<ConversionResult> RunPreparedAsync(RecoveryJob job, IProgress<ConversionProgress>? progress,
        CancellationToken token, Func<OutputConflict, CancellationToken, Task<ConflictDecision>>? resolveConflict)
        => ResumePassAsync(job, progress, token, resolveConflict, null, freshSelection: true);

    private Task<ConversionResult> ResumePassAsync(RecoveryJob job, IProgress<ConversionProgress>? progress,
        CancellationToken token, Func<OutputConflict, CancellationToken, Task<ConflictDecision>>? resolveConflict,
        ConversionConflictReview? review, bool freshSelection)
        => Task.Run(async () =>
        {
            using var run = new RunSession(recovery, job.JournalPath);
            if (run.Data.Finished)
                throw new IOException("This run has already finished.");
            token.ThrowIfCancellationRequested();
            run.RecoverPending();
            if (!freshSelection && !run.Data.ContinueBatchAfterRecovery)
            {
                run.ThrowIfUnresolved();
                var results = run.Data.Results.Values.Concat(run.Data.MetadataResults.Values)
                    .Concat(run.Data.Sources.Where(source => !run.Data.Results.ContainsKey(source))
                        .Select(source => new ConversionFileResult(source, ConversionStatus.Skipped, [], "Left for a new run after recovery.")))
                    .ToArray();
                run.Finish();
                return new ConversionResult(results, false) { Found = run.Data.Sources.Length };
            }
            return await RunCoreAsync(run.Data.Request, run.Data.Policy, progress, token, resolveConflict, run,
                run.Data.ValidateBeforeConversion, run.Data.Policy == OverwritePolicy.Ask ? review : null);
        }, CancellationToken.None);

    /// <summary>Converts selected source groups through verified staging and durable per-group checkpoints.</summary>
    /// <remarks>Cancellation preserves previously published groups. Per-source outcomes are reported in the result;
    /// request/preflight failures can throw. Ask requires a conflict resolver when an overwrite decision arises.</remarks>
    public Task<ConversionResult> RunAsync(ConversionRequest request,
        OverwritePolicy overwrite = OverwritePolicy.Fail, IProgress<ConversionProgress>? progress = null,
        CancellationToken cancellationToken = default,
        Func<OutputConflict, CancellationToken, Task<ConflictDecision>>? resolveConflict = null, bool validateBeforeConversion = false,
        bool continueBatchAfterRecovery = true, FileCheckTiming checkTiming = FileCheckTiming.BeforeItem,
        RecoveryVerificationMode verificationMode = OperationCapabilities.DefaultFileMoveRecoveryVerification)
        => Task.Run(() => RunCoreAsync(request, overwrite, progress, cancellationToken, resolveConflict,
            validateBeforeConversion: validateBeforeConversion, continueBatchAfterRecovery: continueBatchAfterRecovery,
            checkTiming: checkTiming, verificationMode: verificationMode), CancellationToken.None);

    private async Task<ConversionResult> RunCoreAsync(ConversionRequest request, OverwritePolicy overwrite,
        IProgress<ConversionProgress>? progress, CancellationToken token,
        Func<OutputConflict, CancellationToken, Task<ConflictDecision>>? resolveConflict, RunSession? resumed = null, bool validateBeforeConversion = false,
        ConversionConflictReview? review = null, bool continueBatchAfterRecovery = true, FileCheckTiming checkTiming = FileCheckTiming.BeforeItem,
        RecoveryVerificationMode verificationMode = OperationCapabilities.DefaultFileMoveRecoveryVerification)
    {
        request.Validate();
        if (!Enum.IsDefined(checkTiming)) throw new ArgumentOutOfRangeException(nameof(checkTiming));
        if (!Enum.IsDefined(verificationMode)) throw new ArgumentOutOfRangeException(nameof(verificationMode));
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
        if (request.Action is Action.BinToAudioG or Action.BinToZip or Action.LosslessGToMp3G)
        {
            // This local workflow never invokes deferred online providers. Legacy preferences are retained only for compatibility.
            if (request.Action == Action.LosslessGToMp3G)
            {
                if (!AudioEncoderService.SupportedBitrates.Contains(request.Options.AudioBitrate))
                    throw new ArgumentOutOfRangeException("AudioBitrate");
            }
            else AudioEncoderService.Validate(request.Options.AudioEncoder, request.Options.AudioBitrate);
        }
        token.ThrowIfCancellationRequested();
        string[] folders = EnumerateFolders(Path.GetFullPath(request.Folder), request.Options.RecurseFolders).ToArray();
        bool maintenance = OperationCapabilities.RenamesFilenames(request.Action) || request.Action == Action.CreateCueAndTextFiles;
        bool standaloneMedia = !maintenance && WorkflowSession.Current.Value == null;
        string extension = request.Action switch
        {
            Action.AudioGToBin or Action.AudioGToZip or Action.LosslessGToMp3G => ".cdg",
            Action.RepackZip or Action.ZipToBin or Action.ZipToAudioG => ".zip",
            _ => ".bin"
        };
        string[] sources = resumed?.Data.Sources ?? (maintenance ? folders : folders.SelectMany(Directory.GetFiles).Where(p =>
            request.Action == Action.KaraokeToVideo ? IsVideoSource(p) :
            request.Action == Action.ArchiveToZip ? ArchiveZipConverter.IsInput(p) :
            Path.GetExtension(p).Equals(extension, StringComparison.OrdinalIgnoreCase)).Order(StringComparer.OrdinalIgnoreCase).ToArray());
        Dictionary<string, ConversionInputSnapshot>? batchInputs = null;
        var verification = resumed?.Data.VerificationMode ??
            (standaloneMedia ? verificationMode : RecoveryVerificationMode.Sha256);
        if (resumed == null && checkTiming == FileCheckTiming.BeforeBatch && standaloneMedia)
        {
            progress?.Report(new(0, 0, sources.Length, 0, "Checking conversion inputs before the batch…", "00:00:00") { IsIndeterminate = true });
            batchInputs = await ConversionInputSnapshot.CaptureAsync(request.Action, sources, token, verification);
        }
        using var fresh = resumed == null
            ? new RunSession(recovery, request, overwrite, sources, validateBeforeConversion, continueBatchAfterRecovery, batchInputs, verification)
            : null;
        var run = resumed ?? fresh!;
        RunSession.Current.Value = run;
        var results = run.Data.Results.Values.Concat(run.Data.MetadataResults.Values.Where(r => r.Status is ConversionStatus.Failed or ConversionStatus.Skipped)).ToList();
        var watch = Stopwatch.StartNew();
        bool cancelled = false;
        void Report(int percent, string message, string? source = null, string? label = null, string? detail = null, bool show = true)
            => progress?.Report(new(percent, sources.Length == 0 ? 100 : Math.Min(100, results.Count * 100 / sources.Length),
                sources.Length, results.Count, message, watch.Elapsed.ToString(@"hh\:mm\:ss"))
                { ActivitySource = source, ActivityLabel = label, ActivityDetail = detail, ShowInActivity = show });
        void ReportWriting(int percent, string folder, string label)
            => Report(percent, $"{label}: {folder}", folder, label);
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
                Report(0, "Processing: " + source, source, "Processing");
                var snapshot = run.Data.BatchInputs?[source];
                if (snapshot?.Error is { } inputError)
                    throw new IOException(inputError);
                if (review is not { IsApplying: true } && validateBeforeConversion && !maintenance &&
                    request.Action is not (Action.ArchiveToZip or Action.RepackZip))
                {
                    Report(0, "Validating input: " + source, show: false);
                    var validation = await new ValidationService().ScanConversionAsync(source, verification, token);
                    token.ThrowIfCancellationRequested();
                    var issue = validation.Findings.FirstOrDefault(f => f.Status != ValidationStatus.Valid);
                    if (issue != null)
                        throw new InvalidDataException($"Input validation ({issue.Status}): {issue.Message}");
                    if (validation.Findings.Count == 0)
                        throw new InvalidDataException("Input validation found no source group.");
                }
                ConversionFileResult result;
                if (review is { IsApplying: true })
                    result = review.Publish(source, token);
                else if (OperationCapabilities.RenamesFilenames(request.Action))
                    result = await FilenameCleanup.RunAsync(source, request.Action, token);
                else if (request.Action == Action.CreateCueAndTextFiles)
                    result = await WriteMetadataAsync(source, request.Options, overwrite, token, resolveConflict,
                        writing: label => ReportWriting(0, source, label), review: review);
                else
                    result = await ConvertOneAsync(source, request, overwrite, token,
                        (percent, message) => Report(percent, message, show: false), resolveConflict, review, snapshot, verification);
                run.Complete(result);
                results.Add(result);
                Report(100, result.Status + ": " + source + " — " + result.Message, source, result.Status.ToString(), result.Message,
                    show: result.Status != ConversionStatus.Succeeded);
            }
            catch (OperationCanceledException)
            {
                results.Add(new(source, ConversionStatus.Cancelled, [], "Cancelled before publication."));
                cancelled = true;
                break;
            }
            catch (ConversionConflictDeferredException) { /* No publication or completion receipt until review. */ }
            catch (OutputSkippedException) { var result = new ConversionFileResult(source, ConversionStatus.Skipped, [], "Existing output retained."); run.Complete(result); results.Add(result); Report(100, "Skipped: " + source, source, "Skipped", result.Message); }
            catch (Exception error)
            {
                run.ThrowIfUnresolved();
                var result = new ConversionFileResult(source, ConversionStatus.Failed, [], error.Message);
                run.Complete(result);
                results.Add(result);
                Report(100, "Failed: " + source + " — " + error.Message, source, "Failed", error.Message);
            }
        }
        if (!maintenance && request.Action is not (Action.ArchiveToZip or Action.RepackZip or Action.KaraokeToVideo) && request.Options.AutoCreateCueText && !cancelled)
        {
            string listingExtension = request.Action switch
            {
                Action.AudioGToBin or Action.ZipToBin => ".bin",
                Action.AudioGToZip or Action.BinToZip => ".zip",
                _ => ".cdg"
            };
            var listingSources = results.Where(r => r.Status == ConversionStatus.Succeeded).Select(r => r.Source);
            if (review is { IsApplying: false })
                listingSources = listingSources.Concat(review.DeferredSources);
            foreach (string folder in listingSources.Select(source => string.IsNullOrEmpty(request.OutputFolder)
                ? Path.GetDirectoryName(source)! : request.OutputFolder).Distinct(InputSelection.Paths).ToArray())
            {
                if (run.Data.Metadata.Contains(folder))
                    continue;
                run.Begin(folder, metadata: true);
                try
                {
                    if (review is { IsApplying: false })
                    {
                        review.PlanListing(folder, CueTextService.PlannedTargets(folder, request.Options, listingExtension), verification);
                        continue;
                    }
                    var metadata = await WriteMetadataAsync(folder, request.Options, overwrite, token, resolveConflict, listingExtension,
                        label => ReportWriting(100, folder, label), review, verification);
                    run.Complete(metadata);
                }
                catch (OperationCanceledException) { cancelled = true; break; }
                catch (OutputSkippedException) { var result = new ConversionFileResult(folder, ConversionStatus.Skipped, [], "Existing CUE/text retained.") { IsMetadata = true }; run.Complete(result); results.Add(result); }
                catch (Exception error) { run.ThrowIfUnresolved(); var result = new ConversionFileResult(folder, ConversionStatus.Failed, [], "CUE/text: " + error.Message) { IsMetadata = true }; run.Complete(result); results.Add(result); }
            }
        }
        run.ThrowIfUnresolved();
        if (cancelled || token.IsCancellationRequested || review is not { IsApplying: false, HasPendingWork: true })
            run.Finish();
        RunSession.Current.Value = null;
        return new(results, cancelled || token.IsCancellationRequested)
        {
            Found = sources.Length,
            Elapsed = watch.Elapsed,
            RetryRequests = results.Where(r => r.Status == ConversionStatus.Failed)
                .GroupBy(r => r.IsMetadata ? Action.CreateCueAndTextFiles : request.Action)
                .Select(g => new ConversionRetry(g.Key, g.Select(r => r.Source).Distinct(InputSelection.Paths).ToArray(),
                    request.Options with
                    {
                        RecurseFolders = false
                    }, overwrite)
                {
                    ValidateBeforeConversion = validateBeforeConversion,
                    OutputFolder = request.OutputFolder,
                    ContinueBatchAfterRecovery = run.Data.ContinueBatchAfterRecovery,
                    CheckTiming = run.Data.BatchInputs == null ? FileCheckTiming.BeforeItem : FileCheckTiming.BeforeBatch,
                    RecoveryVerification = verification
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
    internal static async Task<ConversionFileResult> WriteMetadataAsync(string folder, ConversionOptions options, OverwritePolicy overwrite, CancellationToken token,
        Func<OutputConflict, CancellationToken, Task<ConflictDecision>>? resolveConflict, string? extension = null,
        System.Action<string>? writing = null, ConversionConflictReview? review = null, RecoveryVerificationMode verification = RecoveryVerificationMode.Sha256)
    {
        using var transaction = new FileTransaction(folder, verification);
        var outputs = CueTextService.Write(folder, transaction.Workspace, options, extension, writing);
        if (outputs.Count == 0)
            return new(folder, ConversionStatus.Skipped, [], "No tracks for CUE/text output.");
        transaction.ObserveTargets(outputs);
        const string message = "Created CUE/text files.";
        if (review is { IsApplying: true })
            overwrite = review.ResolveListing(folder, transaction);
        else if (review != null)
        {
            review.DeferIfConflicting(folder, folder, transaction, outputs, [], message, token);
            overwrite = OverwritePolicy.Fail;
        }
        else overwrite = await ResolveOverwriteAsync(folder, outputs, overwrite, resolveConflict, token);
        var committed = transaction.Commit(outputs, [], overwrite, token);
        return new(folder, ConversionStatus.Succeeded, committed, message);
    }
    private static async Task<ConversionFileResult> ConvertOneAsync(string source, ConversionRequest request, OverwritePolicy overwrite, CancellationToken token, System.Action<int, string> report,
        Func<OutputConflict, CancellationToken, Task<ConflictDecision>>? resolveConflict, ConversionConflictReview? review,
        ConversionInputSnapshot? snapshot, RecoveryVerificationMode verification)
    {
        string destination = string.IsNullOrEmpty(request.OutputFolder) ? Path.GetDirectoryName(source)! : request.OutputFolder;
        using var transaction = new FileTransaction(destination, verification);
        Task<string> StageAsync(string path) => transaction.CopyInputAsync(path, token, snapshot?.FingerprintFor(path));
        string staged = await StageAsync(source);
        var inputs = new List<string> { source };
        var outputs = new List<string>();
        string audio = "", graphics = "";
        if (request.Action is Action.AudioGToBin or Action.AudioGToZip)
        {
            graphics = staged;
            KaraokeArchive.ValidateGraphics(graphics);
            string companion = ConversionInputSnapshot.FindAudioCompanion(request.Action, source)!;
            inputs.Add(companion);
            audio = await StageAsync(companion);
        }
        if (request.Action == Action.LosslessGToMp3G)
        {
            graphics = staged;
            KaraokeArchive.ValidateGraphics(graphics);
            string companion = ConversionInputSnapshot.FindAudioCompanion(request.Action, source)!;
            string stagedAudio = await StageAsync(companion);
            inputs.Add(companion);
            string mp3 = Path.ChangeExtension(staged, ".mp3");
            report(35, "Encoding MP3: " + source);
            await LosslessAudioTranscoder.EncodeAsync(stagedAudio, mp3, request.Options.AudioBitrate, token);
            outputs.Add(mp3);
            if (!string.IsNullOrEmpty(request.OutputFolder)) outputs.Add(graphics);
        }
        if (request.Action is Action.ZipToAudioG or Action.ZipToBin)
        {
            (audio, graphics) = await KaraokeArchive.ExtractAsync(staged, token);
            if (request.Action == Action.ZipToAudioG)
                outputs.AddRange([audio, graphics]);
        }
        if (request.Action is Action.BinToAudioG or Action.BinToZip)
        {
            graphics = Path.ChangeExtension(staged, ".cdg");
            string wav = Path.ChangeExtension(staged, ".wav");
            await BinConverter.SplitAsync(staged, wav, graphics, token);
            audio = Path.ChangeExtension(staged, AudioEncoderService.Extension(request.Options.AudioEncoder));
            report(35, "Encoding: " + source);
            if (request.Options.AudioEncoder != AudioEncoder.WAV)
                await AudioEncoderService.EncodeAsync(wav, audio, request.Options.AudioEncoder, request.Options.AudioBitrate, token);
            if (request.Action == Action.BinToAudioG)
                outputs.AddRange([audio, graphics]);
        }
        if (request.Action is Action.AudioGToBin or Action.ZipToBin)
        {
            string pcm = Path.Combine(transaction.Workspace, "decoded.pcm");
            report(25, "Decoding: " + source);
            await AudioDecoderService.DecodeAsync(audio, pcm, token);
            string bin = Path.ChangeExtension(graphics, ".bin");
            await BinConverter.CombineAsync(pcm, graphics, bin, token);
            BinConverter.Validate(bin);
            outputs.Add(bin);
        }
        if (request.Action is Action.AudioGToZip or Action.BinToZip)
        {
            string zip = Path.ChangeExtension(staged, ".zip");
            report(60, "Creating ZIP: " + source);
            await KaraokeZipWriter.CreateAsync(audio, graphics, zip, request.Options.ZipCompression, token, request.Options.ZipAlgorithm);
            await KaraokeArchive.VerifyCreatedAsync(zip, audio, graphics, token, verification);
            outputs.Add(zip);
        }
        if (request.Action == Action.ArchiveToZip)
        {
            string zip = Path.Combine(transaction.Workspace, ArchiveZipConverter.OutputName(source));
            report(20, "Converting archive: " + source);
            await ArchiveZipConverter.ConvertAsync(staged, zip, request.Options, token, verification: verification);
            outputs.Add(zip);
        }
        if (request.Action == Action.RepackZip)
        {
            string zip = Path.Combine(transaction.Workspace, Path.GetFileNameWithoutExtension(source) + " (repacked).zip");
            report(20, "Repacking ZIP: " + source);
            await ArchiveZipConverter.ConvertAsync(staged, zip, request.Options, token, repackZip: true, verification: verification);
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
        if (request.Action == Action.KaraokeToVideo)
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
                string companion = ConversionInputSnapshot.FindAudioCompanion(request.Action, source)!;
                inputs.Add(companion);
                audio = await StageAsync(companion);
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
        // The CDG remains the graphics half of MP3+G; only the old lossless audio may be removed.
        var retired = request.Options.DeleteOldFiles && request.Action != Action.KaraokeToVideo
            ? request.Action == Action.LosslessGToMp3G ? inputs.Skip(1).ToArray() : inputs.ToArray()
            : [];
        const string message = "Outputs verified and published.";
        // Intentional in-place repacking replaces the verified input, without an overwrite question.
        if (request.Action == Action.RepackZip && request.Options.DeleteOldFiles)
            overwrite = OverwritePolicy.Fail;
        else if (review != null)
        {
            review.DeferIfConflicting(source, destination, transaction, outputs, retired, message, token);
            overwrite = OverwritePolicy.Fail;
        }
        else overwrite = await ResolveOverwriteAsync(destination, outputs, overwrite, resolveConflict, token);
        var committed = transaction.Commit(outputs, retired, overwrite, token);
        return new(source, ConversionStatus.Succeeded, committed, message);
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





