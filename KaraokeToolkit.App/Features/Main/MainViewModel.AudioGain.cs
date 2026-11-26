using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using KaraokeToolkit.App.Features.AudioGain;
using KaraokeToolkit.Core.Audio;
using KaraokeToolkit.Core.Recovery;
using KaraokeToolkit.Core.Reporting;

namespace KaraokeToolkit.App.Features.Main;

public partial class MainViewModel
{
    internal Func<Task<string?>>? PickAudioGainDestinationAsync { get; set; }
    internal Func<AudioGainViewModel, Task<AudioGainPlan?>>? ConfigureAudioGainAsync { get; set; }

    private async Task RunAudioGainAsync(RecoveryJob? job = null)
    {
        bool key = job == null ? SelectedOperation?.IsAudioKey == true : job.Action == "Change Audio Key";
        string title = key ? "Change Audio Key" : "Normalize Audio Gain";
        string action = key ? "Key change" : "Audio gain";
        string reportType = key ? "Audio Key Report" : "Audio Gain Report";
        ProcessingRun? run = null;
        IsBusy = true;
        try
        {
            AudioGainPlan? plan = null;
            if (job == null)
            {
                if (PickAudioGainDestinationAsync == null || ConfigureAudioGainAsync == null)
                    throw new InvalidOperationException("The audio gain preview is unavailable.");
                if (await PickAudioGainDestinationAsync() is not { } outputFolder) return;
                plan = await ConfigureAudioGainAsync(new(CurrentInputs, Options.RecurseFolders, outputFolder, key));
                if (plan == null) return;
            }
            else SelectedOperation = Operations.Single(choice => key ? choice.IsAudioKey : choice.IsAudioGain);

            run = BeginRun(job == null ? title : "Recovery — " + title,
                job == null ? "Preparing audio copies…" : "Checking interrupted audio processing…", isIndeterminate: true);
            IsRecoveryMode = job != null;
            if (job != null) RecoveryMessage = "Recovery mode — resuming the approved audio plan.";
            var service = new AudioGainService(recovery);
            var result = job == null
                ? await service.ApplyAsync(plan!, run.Progress, run.Token)
                : await service.ResumeAsync(job, run.Progress, run.Token);
            run.StopProgress();
            LastReport = CompletionReport.FromConversion(title, result) with { ReportType = reportType };
            CompleteActivityItemCount(result);
            Status = $"{action} {(result.Cancelled ? "cancelled" : "finished")} · {result.Succeeded} copied · {result.Skipped} skipped · {result.Failed} failed";
            StatusOutcome = RunStatusSummary.From(result);
            if (!result.Cancelled) Progress = TotalProgress = 100;
            AppendActivityIssues(result.Files);
            AddActivity(Status);
        }
        catch (OperationCanceledException)
        {
            Status = action + " cancelled.";
            StatusOutcome = RunStatus.Neutral;
        }
        catch (Exception error)
        {
            Status = action + " stopped. " + error.Message;
            StatusOutcome = RunStatus.Failure;
            if (run != null)
                LastReport = new(title, DateTimeOffset.Now, false, [new(FolderPath, "Failed", error.Message, [])]) { ReportType = reportType };
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
