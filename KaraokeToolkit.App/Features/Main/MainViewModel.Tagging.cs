using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KaraokeToolkit.App.Features.Tagging;
using KaraokeToolkit.App.Shared;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Reporting;
using KaraokeToolkit.Core.Tagging;
using KaraokeToolkit.Core.Recovery;

namespace KaraokeToolkit.App.Features.Main;

public partial class MainViewModel
{
    public Func<TaggingViewModel, Task<TaggingRequest?>>? ConfigureTaggingAsync { get; set; }
    private async Task RunTaggingAsync(RecoveryJob? job = null)
    {
        IsBusy = true;
        try
        {
            TaggingRequest? request = null;
            if (job == null)
            {
                if (ConfigureTaggingAsync == null) throw new InvalidOperationException("The tagging dialog is unavailable.");
                int recovered = await new KaraokeTaggingService(recovery).RecoverInterruptedPublicationsAsync(CurrentInputs, Options.RecurseFolders);
                var dialog = new TaggingViewModel(CurrentInputs, Options.RecurseFolders, store.LoadTaggingPatterns(), store.LoadLastTaggingPattern());
                if (recovered > 0) dialog.RecoveryNotice = $"Recovery: {recovered} interrupted tagging publication(s) checked. Review a fresh preview before continuing.";
                request = await ConfigureTaggingAsync(dialog);
                if (request == null) return;
            }
            // Remember only combinations accepted by the user, preserving processing settings atomically.
            if (request != null) store.Save(Options, taggingPatterns: store.LoadTaggingPatterns().Append(request.Pattern).Distinct(StringComparer.Ordinal).ToArray(), lastTaggingPattern: request.Pattern);
            using var source = new CancellationTokenSource();
            cancellation = source;
            CancelRequested = false;
            ShowProgress = true;
            IsRecoveryMode = job != null;
            RecoveryMessage = job == null ? "" : "Recovery mode — resuming the saved tagging preview and remaining files.";
            if (job != null) SelectedOperation = Operations.Single(o => o.IsTagging);
            RunTitle = job == null ? "Tag Karaoke Files" : "Recovery — Tag Karaoke Files";
            Activity.Clear(); lastMessage = ""; LastReport = null; retryRequests = []; ValidationFindings = [];
            Progress = TotalProgress = FoundFiles = 0; Elapsed = "00:00:00";
            Status = "Applying tags…";
            bool accepting = true;
            var gate = new object();
            var reporter = new Progress<ConversionProgress>(p => { lock (gate) { if (accepting) Report(p); } });
            ConversionResult result;
            try
            {
                var service = new KaraokeTaggingService(recovery);
                result = job == null ? await service.ApplyAsync(request!.Files, request.Options, Options, reporter, source.Token)
                    : await service.ResumeAsync(job, reporter, source.Token);
            }
            finally { lock (gate) { accepting = false; } }
            LastReport = CompletionReport.FromConversion(RunTitle, result) with { ReportType = "Tagging Report" };
            FoundFiles = result.Found; Elapsed = result.Elapsed.ToString(@"hh\:mm\:ss");
            if (!result.Cancelled) Progress = TotalProgress = 100;
            Status = $"Tagging {(result.Cancelled ? "cancelled" : "finished")}: {result.Succeeded} updated, {result.Skipped} skipped, {result.Failed} failed.";
            StatusOutcome = RunStatusSummary.From(result);
            foreach (var file in result.Files) Activity.Add(new(Elapsed, $"{file.Status}: {file.Source} — {file.Message}"));
            Activity.Add(new(Elapsed, Status));
        }
        catch (Exception error)
        {
            StatusOutcome = RunStatus.Failure; Status = "Tagging stopped: " + error.Message;
            if (ShowProgress) LastReport = new("Tag Karaoke Files", DateTimeOffset.Now, false, [new ReportEntry(FolderPath, "Failed", error.Message, [])]) { ReportType = "Tagging Report" };
        }
        finally
        {
            cancellation = null; IsBusy = false; IsRecoveryMode = false;
            try { RecoveryAvailable = recovery.Find().Count > 0; RecoveryMessage = RecoveryAvailable ? "An interrupted workflow remains. Use Recover to review it." : ""; }
            catch { /* Keep the tagging outcome visible. */ }
        }
    }
}
