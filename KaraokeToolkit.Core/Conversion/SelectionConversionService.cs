using KaraokeToolkit.Core.Execution;
using KaraokeToolkit.Core.Recovery;
namespace KaraokeToolkit.Core.Conversion;

public sealed record ConversionRetry(OperationId Action, string[] Inputs, ConversionOptions Options, OverwritePolicy Policy)
{
    public bool ValidateBeforeConversion
    {
        get; init;
    }
    public string OutputFolder { get; init; } = "";
    public bool ContinueBatchAfterRecovery { get; init; } = true;
    public FileCheckTiming CheckTiming { get; init; } = FileCheckTiming.BeforeItem;
    public RecoveryVerificationMode RecoveryVerification { get; init; } = OperationCapabilities.DefaultFileMoveRecoveryVerification;
}

/// <summary>Creates every folder checkpoint before starting a multi-folder selection.</summary>
public sealed class SelectionConversionService(RecoveryStore? recovery = null)
{
    private readonly RecoveryStore recovery = recovery ?? new();

    /// <summary>Plans the selected source groups and prepares folder checkpoints before running conversions.</summary>
    /// <remarks>Aggregates per-folder outcomes and retry requests while preserving completed work on cancellation.</remarks>
    public async Task<ConversionResult> RunAsync(ConversionRetry request, IProgress<ConversionProgress>? progress = null,
        CancellationToken token = default, Func<OutputConflict, CancellationToken, Task<ConflictDecision>>? resolveConflict = null,
        ResolveOutputConflicts? resolveConflicts = null)
    {
        return await Task.Run(async () =>
        {
            var workflow = WorkflowSession.Current.Value;
            if (!Enum.IsDefined(request.CheckTiming)) throw new ArgumentOutOfRangeException(nameof(request.CheckTiming));
            if (!Enum.IsDefined(request.RecoveryVerification)) throw new ArgumentOutOfRangeException(nameof(request.RecoveryVerification));
            var plans = workflow?.Data.Conversions is { } saved
                ? saved.Select(p => new SelectionBatch(p.Folder, p.Sources)).ToArray()
                : InputSelection.Plan(request.Action, request.Inputs, request.Options.RecurseFolders);
            if (workflow != null && workflow.Data.Conversions == null)
            {
                workflow.Data.Conversions = plans.Select(p => new WorkflowConversion { Folder = p.Folder, Sources = p.Sources.ToArray() }).ToList();
                workflow.Save(); // Freeze all folder/source plans before the first mutation.
            }
            var prepared = new List<RecoveryJob>();
            bool standaloneMedia = workflow == null && OperationCapabilities.SupportsConversionCheckTiming(request.Action);
            bool captureBatchInputs = standaloneMedia && request.CheckTiming == FileCheckTiming.BeforeBatch;
            var verification = standaloneMedia ? request.RecoveryVerification : RecoveryVerificationMode.Sha256;
            if (captureBatchInputs)
                progress?.Report(new(0, 0, plans.Sum(plan => plan.Sources.Count), 0, "Checking conversion inputs before the batch…", "00:00:00")
                    { IsIndeterminate = true });
            try
            {
                foreach (var plan in plans)
                {
                    token.ThrowIfCancellationRequested();
                    var savedPlan = workflow?.Data.Conversions?.Single(p => InputSelection.Paths.Equals(p.Folder, plan.Folder));
                    if (savedPlan?.Result != null)
                    {
                        prepared.Add(new(savedPlan.Journal ?? "", plan.Folder, request.Action.ToString(), plan.Sources.Count, plan.Sources.Count));
                        continue;
                    }
                    if (savedPlan?.Journal != null)
                    {
                        if (!File.Exists(savedPlan.Journal)) throw new IOException("The recipe's child conversion journal is missing: " + savedPlan.Journal);
                        var child = RunSession.Read(savedPlan.Journal);
                        workflow!.RecordChild(savedPlan.Journal, child);
                        prepared.Add(new(savedPlan.Journal, plan.Folder, request.Action.ToString(), child.Results.Count, child.Sources.Length));
                        continue;
                    }
                    var operation = new ConversionRequest(request.Action, plan.Folder, request.Options with
                    {
                        RecurseFolders = false
                    }) { OutputFolder = request.OutputFolder };
                    operation.Validate();
                    if (OperationCapabilities.Describe(request.Action) != OperationSupport.Supported)
                        throw new NotSupportedException("This operation is unavailable.");
                    string[] sources = plan.Sources.ToArray();
                    var batchInputs = captureBatchInputs
                        ? await ConversionInputSnapshot.CaptureAsync(request.Action, sources, token, verification)
                        : null;
                    using var run = new RunSession(recovery, operation, request.Policy, sources, request.ValidateBeforeConversion,
                        request.ContinueBatchAfterRecovery, batchInputs, verification);
                    prepared.Add(new(run.Journal, plan.Folder, request.Action.ToString(), 0, plan.Sources.Count));
                }
            }
            catch (Exception error)
            {
                if (workflow != null && error is not OperationCanceledException) throw; // Keep unresolved workflow work for recovery.
                foreach (var job in prepared)
                {
                    if (!File.Exists(job.JournalPath)) continue;
                    using var run = new RunSession(recovery, job.JournalPath);
                    run.ThrowIfUnresolved();
                    run.Finish();
                }
                throw;
            }

            if (workflow == null && request.Policy == OverwritePolicy.Ask && resolveConflicts != null)
                return await new ConversionService(recovery).RunWithConflictReviewAsync(prepared, resolveConflicts, progress, token);

            var results = new List<ConversionFileResult>();
            var retry = new List<ConversionRetry>();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            int found = plans.Sum(p => p.Sources.Count), completed = 0;
            bool cancelled = false;
            for (int i = 0; i < prepared.Count; i++)
            {
                var completedPlan = workflow?.Data.Conversions?[i].Result;
                if (completedPlan != null)
                {
                    string? finishedJournal = workflow!.Data.Conversions![i].Journal;
                    if (finishedJournal != null && File.Exists(finishedJournal))
                    { using var finished = new RunSession(recovery, finishedJournal); finished.RecoverPending(); finished.Finish(); }
                    results.AddRange(completedPlan.Files); completed += completedPlan.Found;
                    continue;
                }
                if (token.IsCancellationRequested)
                {
                    cancelled = true;
                }
                if (cancelled)
                {
                    using var pending = new RunSession(recovery, prepared[i].JournalPath);
                    pending.Finish(); // Ordinary cancellation is not a crash.
                    continue;
                }
                var reporter = new InlineProgress(p => progress?.Report(p with
                {
                    Found = found,
                    Completed = completed + p.Completed,
                    TotalPercent = found == 0 ? 100 : Math.Min(100, (completed + p.Completed) * 100 / found)
                }));
                ConversionResult result;
                try
                {
                    result = await new ConversionService(recovery).RunPreparedAsync(prepared[i], reporter, token, resolveConflict);
                }
                catch (OperationCanceledException)
                {
                    using var pending = new RunSession(recovery, prepared[i].JournalPath);
                    pending.ThrowIfUnresolved();
                    pending.Finish();
                    cancelled = true;
                    continue;
                }
                results.AddRange(result.Files);
                retry.AddRange(result.RetryRequests);
                completed += result.Found;
                cancelled = result.Cancelled;
            }
            return new ConversionResult(results, cancelled) { Found = found, Elapsed = watch.Elapsed, RetryRequests = retry };
        }, CancellationToken.None);
    }

    internal sealed class InlineProgress(System.Action<ConversionProgress> action) : IProgress<ConversionProgress>
    {
        public void Report(ConversionProgress value) => action(value);
    }
}
