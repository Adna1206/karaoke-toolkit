using KaraokeToolkit.App.Features.FilenameRename;
using KaraokeToolkit.Core.FileOperations;
using KaraokeToolkit.Core.Reporting;
using System;
using System.IO;
using System.Threading.Tasks;

namespace KaraokeToolkit.App.Features.Main;

public partial class MainViewModel
{
    internal Func<FilenameRenameViewModel, Task<FilenameRenameSubmission?>>? ConfigureFilenameRenameAsync { get; set; }

    private async Task RunFilenameRenameAsync()
    {
        const string title = "Rename by Filename Pattern";
        ProcessingRun? run = null;
        try
        {
            foreach (string input in CurrentInputs)
                if (await CheckForRecoveryAsync(Directory.Exists(input) ? input : Path.GetDirectoryName(Path.GetFullPath(input))!))
                    return;

            if (ConfigureFilenameRenameAsync == null) throw new InvalidOperationException("The rename preview is unavailable.");
            IsBusy = true;
            var model = new FilenameRenameViewModel(CurrentInputs, Options.RecurseFolders, store.LoadTaggingPatterns());
            var submission = await ConfigureFilenameRenameAsync(model);
            if (submission == null) return;

            run = BeginRun(title, "Preparing recovery for renames…", isIndeterminate: true);
            var result = await new FilenameRenameService(recovery).ApplyAsync(submission.Request, submission.Plan,
                MoveRecoveryVerification, SaveResumableRun, run.Progress, run.Token, CheckTiming);
            run.StopProgress();
            LastReport = CompletionReport.FromConversion(title, result) with { ReportType = "Filename Rename Report" };
            CompleteActivityItemCount(result);
            Status = $"{(result.Cancelled ? "Rename cancelled" : "Rename finished")} · {result.Succeeded} renamed · {result.Skipped} skipped · {result.Failed} failed";
            StatusOutcome = RunStatusSummary.From(result);
            if (!result.Cancelled) Progress = TotalProgress = 100;
            AppendActivityIssues(result.Files);
            AddActivity(Status);
        }
        catch (OperationCanceledException)
        {
            Status = "Rename cancelled.";
            StatusOutcome = RunStatus.Neutral;
        }
        catch (Exception error)
        {
            Status = "Rename stopped. " + error.Message;
            StatusOutcome = RunStatus.Failure;
            if (run != null)
                LastReport = new(title, DateTimeOffset.Now, false, [new(FolderPath, "Failed", error.Message, [])])
                    { ReportType = "Filename Rename Report" };
        }
        finally
        {
            EndRun(run);
            try { RecoveryAvailable = recovery.Find().Count > 0; }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }
}
