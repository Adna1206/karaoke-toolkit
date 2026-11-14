using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.FileOperations;
using KaraokeToolkit.Core.Recovery;
using KaraokeToolkit.Core.Reporting;

namespace KaraokeToolkit.Core.Execution;

public sealed partial class OperationExecutionService
{
    public Task<ExecutionResult> ExecuteGroupingAsync(OperationRequest request, TrackGroupingPlan? approvedPlan,
        IProgress<ConversionProgress>? progress = null, CancellationToken token = default) =>
        Task.Run(() => TrackGroupingExecution.Run(recovery, request, approvedPlan, progress, token), token);
}

internal static class TrackGroupingExecution
{
    internal static ExecutionResult Preview(OperationRequest request, CancellationToken token) =>
        Report(Plan(request, token), dryRun: true);

    private static TrackGroupingPlan Plan(OperationRequest request, CancellationToken token)
    {
        if (request.OperationId != OperationId.GroupByTrackId || request.OverwritePolicy != OverwritePolicy.Fail)
            throw new ArgumentException("Track grouping requires the fail-on-conflict policy.");
        if (!Enum.IsDefined(request.RecoveryVerification)) throw new ArgumentException("Unknown recovery verification mode.");
        return TrackGroupingPlanner.Plan(request.Inputs, request.OutputFolder, request.Grouping, token);
    }

    private static ExecutionResult Report(TrackGroupingPlan plan, bool dryRun)
    {
        var entries = plan.Issues.Select(i => new ReportEntry(i.Source, i.IsConflict ? "Blocked" : "Skipped", i.Message, [])).ToList();
        entries.AddRange(plan.Items.Select(i => new ReportEntry(i.Source, dryRun ? "Planned" : "Skipped",
            dryRun ? "Proposed destination." : "Batch blocked by conflicts.", [i.Destination])));
        return new(new(nameof(OperationId.GroupByTrackId), DateTimeOffset.Now, false, entries)
            { ReportType = dryRun ? "Dry Run Report" : "Track Grouping Report" },
            entries.Count, plan.Issues.Any(i => i.IsConflict));
    }

    internal static ExecutionResult Run(RecoveryStore recovery, OperationRequest request, TrackGroupingPlan? approved,
        IProgress<ConversionProgress>? progress, CancellationToken token)
    {
        string output = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.OutputFolder));
        using var lease = FileTransaction.AcquireLease(output);
        progress?.Report(new(0, 0, approved?.Items.Count ?? 0, 0, "Checking grouping plan…", "00:00:00"));
        var plan = Plan(request, token);
        if (approved != null && (!plan.Items.SequenceEqual(approved.Items) || !plan.Issues.SequenceEqual(approved.Issues)))
            throw new IOException("The grouping plan changed. Review a fresh preview before applying it.");
        if (plan.Items.Count == 0 || plan.Issues.Any(i => i.IsConflict))
            return Report(plan, dryRun: false);
        var roots = plan.Items.Select(i => Path.GetDirectoryName(i.Source)!).Prepend(output)
            .Distinct(InputSelection.Paths).ToArray();
        foreach (string root in roots)
            if (recovery.Find(root, true).Count > 0)
                throw new IOException("Recover interrupted work before grouping items in: " + root);

        progress?.Report(new(0, 0, plan.Items.Count, 0, "Preparing recovery for grouped items…", "00:00:00"));
        var groups = plan.Items.GroupBy(i => Path.GetDirectoryName(i.Source), InputSelection.Paths)
            .SelectMany(directory => directory.GroupBy(i => i.IsDirectory
                ? "folder:" + Path.GetFileName(i.Source)
                : "file:" + Path.GetFileNameWithoutExtension(i.Source), StringComparer.OrdinalIgnoreCase))
            .Select(g => new WorkflowMoveGroup { Items = g.Select(i =>
            {
                token.ThrowIfCancellationRequested();
                return WorkflowMoves.Move(i.Source, i.Destination, request.RecoveryVerification);
            }).ToArray() }).ToList();
        Directory.CreateDirectory(output);
        using var session = new WorkflowSession(recovery, new WorkflowData
        {
            Kind = RecoveryKind.TrackGrouping, Roots = roots, CopyGrouping = !request.Grouping.Move,
            OperationId = OperationId.GroupByTrackId, Moves = groups, VerificationMode = request.RecoveryVerification, SaveResumableRun = request.SaveResumableRun
        });
        var result = Apply(session, progress, token);
        return result with { Found = result.Found + plan.Issues.Count, Report = result.Report with
        {
            Entries = result.Report.Entries.Concat(plan.Issues.Select(i => new ReportEntry(i.Source, "Skipped", i.Message, []))).ToArray()
        } };
    }

    internal static ExecutionResult Resume(RecoveryStore recovery, RecoveryJob job, IProgress<ConversionProgress>? progress, CancellationToken token)
    {
        using var lease = FileTransaction.AcquireLease(job.Folder);
        using var session = new WorkflowSession(recovery, job);
        if (session.Data.Kind != RecoveryKind.TrackGrouping) throw new IOException("This recovery job is not track grouping.");
        session.VerifyExpected();
        return Apply(session, progress, token);
    }

    private static ExecutionResult Apply(WorkflowSession session, IProgress<ConversionProgress>? progress, CancellationToken token)
    {
        var result = session.Data.CopyGrouping
            ? TrackGroupingCopies.Run(session, progress, token)
            : WorkflowMoves.Run(session, null, progress, token);
        session.Finish();
        return new(CompletionReport.FromConversion(nameof(OperationId.GroupByTrackId), result), result.Found, result.Failed > 0 || result.Files.Any(f => f.Status == ConversionStatus.Skipped));
    }
}
