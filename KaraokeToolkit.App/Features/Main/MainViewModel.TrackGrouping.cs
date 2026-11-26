using KaraokeToolkit.App.Features.TrackGrouping;
using KaraokeToolkit.Core.Execution;
using KaraokeToolkit.Core.Recovery;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace KaraokeToolkit.App.Features.Main;

public partial class MainViewModel
{
    internal Func<TrackGroupingViewModel, Task<TrackGroupingSubmission?>>? ConfigureTrackGroupingAsync { get; set; }
    internal Func<Task<string?>>? PickTrackGroupingDestinationAsync { get; set; }

    private async Task RunTrackGroupingAsync(RecoveryJob? job = null)
    {
        const string title = "Group by Track ID";
        ProcessingRun? run = null;
        try
        {
            TrackGroupingSubmission? submission = null;
            if (job == null)
            {
                foreach (string input in CurrentInputs)
                    if (await CheckForRecoveryAsync(Directory.Exists(input) ? input : Path.GetDirectoryName(Path.GetFullPath(input))!))
                        return;
                IsBusy = true;
                if (ConfigureTrackGroupingAsync == null) throw new InvalidOperationException("The grouping preview is unavailable.");
                var preview = new TrackGroupingViewModel(CurrentInputs);
                if (preview.OutputFolder == null)
                {
                    if (PickTrackGroupingDestinationAsync == null)
                        throw new InvalidOperationException("The grouping destination picker is unavailable.");
                    string? destination = await PickTrackGroupingDestinationAsync();
                    if (destination == null) return;
                    preview.SetSelectedDestination(destination);
                }
                submission = await ConfigureTrackGroupingAsync(preview);
                if (submission == null) return;
            }
            else
            {
                SelectedOperation = Operations.Single(choice => choice.IsTrackGrouping);
                SetInputs([job.Folder], suggestOperation: false);
            }

            run = BeginRun(job != null ? "Recovery — " + title : title,
                job != null ? "Checking interrupted grouping…" : "Preparing recovery for grouped items…", isIndeterminate: true);
            IsRecoveryMode = job != null;
            if (job != null)
            {
                RecoveryMessage = job.ContinueBatchAfterRecovery
                    ? "Recovery mode — resuming the approved grouping plan."
                    : "Recovery mode — checking the interrupted group only.";
            }

            var service = new OperationExecutionService(recovery);
            var result = job == null
                ? await service.ExecuteGroupingAsync(submission!.Request with
                {
                    RecoveryVerification = RecoveryVerification, SaveResumableRun = SaveResumableRun, CheckTiming = CheckTiming
                }, submission.Plan, run.Progress, run.Token)
                : await Task.Run(() => service.ResumeAsync(job.JournalPath, run.Progress, run.Token));
            run.StopProgress();
            LastReport = result.Report with { Operation = title, ReportType = "Track Grouping Report" };
            CompleteActivityItemCount(result);
            int completed = result.Report.Entries.Count(entry => entry.Status == "Succeeded");
            int skipped = result.Report.Entries.Count(entry => entry.Status == "Skipped");
            int failed = result.Report.Entries.Count(entry => entry.Status is "Failed" or "Blocked");
            Status = $"{(result.Report.Cancelled ? "Grouping cancelled" : "Grouping finished")} · {completed} completed · {skipped} skipped · {failed} failed";
            StatusOutcome = RunStatusSummary.From(result.Report.Cancelled, completed, failed, skipped);
            if (!result.Report.Cancelled && failed == 0) Progress = TotalProgress = 100;
            var logged = Activity.Select(entry => entry.Message).ToHashSet(StringComparer.Ordinal);
            foreach (var entry in result.Report.Entries)
            {
                string message = entry.Status == "Succeeded"
                    ? FormatActivityMessage("Processing", entry.Source, null)
                    : FormatActivityMessage(entry.Status, entry.Source, entry.Message);
                if (logged.Add(message))
                    AddActivity(message);
            }
            AddActivity(Status);
        }
        catch (OperationCanceledException)
        {
            Status = "Grouping cancelled.";
            StatusOutcome = RunStatus.Neutral;
        }
        catch (Exception error)
        {
            Status = "Grouping stopped. " + error.Message;
            StatusOutcome = RunStatus.Failure;
            if (run != null)
                LastReport = new(title, DateTimeOffset.Now, false, [new(FolderPath, "Failed", error.Message, [])]) { ReportType = "Track Grouping Report" };
        }
        finally
        {
            EndRun(run);
            try
            {
                RecoveryAvailable = recovery.Find().Count > 0;
                RecoveryMessage = RecoveryAvailable ? "An interrupted run remains. Use Recover to review it." : "";
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }
}
