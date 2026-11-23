using System.Diagnostics;
using KaraokeToolkit.Core.Recovery;

namespace KaraokeToolkit.Core.Conversion;

public sealed partial class ConversionService
{
    public Task<ConversionResult> ResumeWithConflictReviewAsync(RecoveryJob job, ResolveOutputConflicts resolve,
        IProgress<ConversionProgress>? progress = null, CancellationToken token = default) =>
        RunWithConflictReviewAsync([job], resolve, progress, token, freshSelection: false);

    internal async Task<ConversionResult> RunWithConflictReviewAsync(IReadOnlyList<RecoveryJob> jobs, ResolveOutputConflicts resolve,
        IProgress<ConversionProgress>? progress, CancellationToken token, bool freshSelection = true)
    {
        var watch = Stopwatch.StartNew();
        var reviews = jobs.Select(job => new ConversionConflictReview(job.JournalPath)).ToArray();
        var outcomes = new ConversionResult?[jobs.Count];
        int found = jobs.Sum(job => job.Total);
        bool cancelled = false;
        for (int i = 0; i < jobs.Count; i++)
        {
            if (token.IsCancellationRequested)
            {
                cancelled = true;
                break;
            }
            try
            {
                outcomes[i] = await ResumePassAsync(jobs[i], Reporter(i), token, null, reviews[i]);
                if (outcomes[i]!.Cancelled)
                {
                    cancelled = true;
                    break;
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                cancelled = true;
                break;
            }
        }

        if (!cancelled)
        {
            var conflicts = reviews.SelectMany(review => review.Conflicts.Select(conflict => (Review: review, Conflict: conflict))).ToArray();
            if (conflicts.Length > 0)
            {
                int completed = CompletedExcept(-1);
                progress?.Report(new(0, Percent(completed), found, completed, "Reviewing existing output files…",
                    watch.Elapsed.ToString(@"hh\:mm\:ss")) { ShowInActivity = false });
                IReadOnlyList<ConflictDecision>? choices;
                try { choices = await resolve(conflicts.Select(item => item.Conflict).ToArray(), token); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { choices = null; }
                if (choices == null || token.IsCancellationRequested)
                    cancelled = true;
                else
                {
                    if (choices.Count != conflicts.Length || choices.Any(choice => choice is not (ConflictDecision.Skip or ConflictDecision.Replace)))
                        throw new InvalidOperationException("Choose Skip or Replace for every output group.");
                    for (int i = 0; i < conflicts.Length; i++)
                        conflicts[i].Review.Decide(conflicts[i].Conflict, choices[i]);
                }
            }
        }

        for (int i = 0; i < jobs.Count; i++)
        {
            cancelled |= token.IsCancellationRequested;
            if (cancelled)
            {
                if (outcomes[i] != null || freshSelection)
                    FinishIfPending(jobs[i]);
                continue;
            }
            if (!reviews[i].HasPendingWork) continue;
            reviews[i].IsApplying = true;
            try
            {
                outcomes[i] = await ResumePassAsync(jobs[i], Reporter(i), token, null, reviews[i]);
                cancelled = outcomes[i]!.Cancelled;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                FinishIfPending(jobs[i]);
                cancelled = true;
            }
        }

        var completedRuns = outcomes.OfType<ConversionResult>().ToArray();
        return new(completedRuns.SelectMany(result => result.Files).ToArray(), cancelled)
        {
            Found = found,
            Elapsed = watch.Elapsed,
            RetryRequests = completedRuns.SelectMany(result => result.RetryRequests).ToArray()
        };

        int CompletedExcept(int index) => outcomes.Where((_, i) => i != index).OfType<ConversionResult>()
            .Sum(result => result.Files.Count(file => !file.IsMetadata));

        int Percent(int completed) => found == 0 ? 100 : Math.Min(100, completed * 100 / found);

        IProgress<ConversionProgress> Reporter(int index) => new SelectionConversionService.InlineProgress(value =>
        {
            int completed = CompletedExcept(index) + value.Completed;
            progress?.Report(value with { Found = found, Completed = completed, TotalPercent = Percent(completed) });
        });
    }

    private void FinishIfPending(RecoveryJob job)
    {
        if (!File.Exists(job.JournalPath)) return;
        using var run = new RunSession(recovery, job.JournalPath);
        run.ThrowIfUnresolved();
        run.Finish();
    }
}
