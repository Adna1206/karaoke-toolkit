using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KaraokeToolkit.App.Shared;
using KaraokeToolkit.Core.FileOperations;
using KaraokeToolkit.Core.Naming;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace KaraokeToolkit.App.Features.FilenameRename;

internal sealed record FilenameRenameSubmission(FilenameRenameRequest Request, FilenameRenamePlan Plan);

public sealed record FilenameRenamePreviewRow(FilenameRenameItem Item)
{
    public string SourceName => Path.GetFileName(Item.Source);
    public string? DestinationName => Path.GetFileName(Item.Destination);
    public string IssueHeading => $"{(Item.IsConflict ? "Conflict" : "Skipped")} · {SourceName}";
    public override string ToString() => Item.IsConflict || Item.Destination == null
        ? $"{IssueHeading}. {Item.Issue}"
        : $"{SourceName} to {DestinationName}";
}

public sealed partial class FilenameRenameViewModel(IReadOnlyList<string> inputs, bool recurse,
    IReadOnlyList<string> patterns) : ViewModelBase
{
    private const string DefaultPattern = KaraokeFilenamePattern.DiscIdTrackArtistTitle;
    private const string DiscTrackTitleArtist = "%DISC%-%TRACK% %TITLE% - %ARTIST%";
    private readonly bool initialRecurse = recurse;
    private readonly PreviewWork<FilenameRenameService.PreviewSnapshot> work = new();
    private FilenameRenameSubmission? preview;

    public IReadOnlyList<string> SourcePatterns { get; } = patterns.Append(DiscTrackTitleArtist).Distinct(StringComparer.Ordinal).ToArray();
    public IReadOnlyList<string> DestinationPatterns { get; } = patterns;
    public PreviewProgress PreviewProgress { get; } = new();

    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanApply))]
    public partial string SourcePattern { get; set; } = DefaultPattern;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanApply))]
    public partial string DestinationPattern { get; set; } = DefaultPattern;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanApply))]
    public partial bool RemoveDiscWhitespace { get; set; } = FilenameRenameRequest.DefaultRemoveDiscWhitespace;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanApply))]
    public partial bool UppercaseDisc { get; set; } = true;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanApply))]
    public partial bool IncludeSubfolders { get; set; } = recurse;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanApply), nameof(CanPreview))]
    public partial bool IsBusy { get; private set; }
    [ObservableProperty]
    public partial IReadOnlyList<FilenameRenamePreviewRow> Changes { get; private set; } = [];
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasIssues))]
    public partial IReadOnlyList<FilenameRenamePreviewRow> Issues { get; private set; } = [];
    [ObservableProperty]
    public partial string Status { get; private set; } = "Preparing preview…";
    [ObservableProperty]
    public partial StatusKind StatusSeverity { get; private set; }

    public bool CanPreview => !IsBusy;
    public bool HasIssues => Issues.Count > 0;
    public bool CanApply => !IsBusy && preview is { Plan.CanApply: true } && preview.Request == Request;
    internal bool HasEdits => SourcePattern != DefaultPattern || DestinationPattern != DefaultPattern ||
        RemoveDiscWhitespace != FilenameRenameRequest.DefaultRemoveDiscWhitespace || !UppercaseDisc || IncludeSubfolders != initialRecurse;
    private FilenameRenameRequest Request => new(inputs, SourcePattern, DestinationPattern,
        RemoveDiscWhitespace, IncludeSubfolders, UppercaseDisc);
    internal Task PendingPreview { get; private set; } = Task.CompletedTask;

    partial void OnSourcePatternChanged(string value) => UpdatePreview();
    partial void OnDestinationPatternChanged(string value) => UpdatePreview();
    partial void OnRemoveDiscWhitespaceChanged(bool value) => UpdatePreview();
    partial void OnUppercaseDiscChanged(bool value) => UpdatePreview();
    partial void OnIncludeSubfoldersChanged(bool value) => UpdatePreview();

    private void UpdatePreview()
    {
        if (work.CanUpdate) PendingPreview = UpdatePreviewAsync(refresh: false, CancellationToken.None);
    }

    [RelayCommand]
    internal Task RefreshAsync(CancellationToken token = default) => UpdatePreviewAsync(refresh: true, token);

    internal void StopPreview()
    {
        work.Stop();
        PreviewProgress.Finish();
    }

    private async Task UpdatePreviewAsync(bool refresh, CancellationToken token)
    {
        using var requestWork = work.Begin(refresh, token);
        if (requestWork == null) return;
        var cancellation = requestWork.Token;
        var request = Request;
        preview = null;
        IsBusy = true;
        Status = "Updating preview…";
        StatusSeverity = StatusKind.Info;
        PreviewProgress.Begin(status => Status = status);
        try
        {
            if (!refresh) await Task.Delay(PreviewWork<FilenameRenameService.PreviewSnapshot>.EditDelay, cancellation);
            var scan = work.ReadAsync(scanToken => Task.Run(() => FilenameRenameService.ReadPreview(inputs, scanToken), scanToken), cancellation);
            if (!scan.IsCompleted) Status = "Scanning inputs…";
            var snapshot = await scan;
            Status = "Planning filenames…";
            var result = await Task.Run(() =>
            {
                var plan = FilenameRenameService.PlanPreview(request, snapshot, cancellation);
                var changes = new List<FilenameRenamePreviewRow>();
                var conflicts = new List<FilenameRenamePreviewRow>();
                var skipped = new List<FilenameRenamePreviewRow>();
                foreach (var item in plan.Items)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var row = new FilenameRenamePreviewRow(item);
                    if (item.IsConflict) conflicts.Add(row);
                    else if (item.Destination == null) skipped.Add(row);
                    else changes.Add(row);
                }
                int conflictCount = conflicts.Count;
                conflicts.AddRange(skipped);
                return (plan, changes, issues: conflicts, conflicts: conflictCount);
            }, cancellation);
            cancellation.ThrowIfCancellationRequested();
            preview = new(request, result.plan);
            Changes = result.changes;
            Issues = result.issues;
            int ready = Changes.Count;
            int skipped = Issues.Count - result.conflicts;
            Status = $"{ready} ready · {skipped} skipped · {result.conflicts} {(result.conflicts == 1 ? "conflict" : "conflicts")}";
            StatusSeverity = result.conflicts > 0 ? StatusKind.Failure : ready == 0 || skipped > 0 ? StatusKind.Warning : StatusKind.Success;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException or FormatException)
        {
            if (!cancellation.IsCancellationRequested)
            {
                Changes = [];
                Issues = [];
                Status = error.Message;
                StatusSeverity = StatusKind.Failure;
            }
        }
        finally
        {
            if (work.Finish(requestWork))
            {
                if (cancellation.IsCancellationRequested) Status = "Preview cancelled.";
                PreviewProgress.Finish();
                IsBusy = false;
            }
        }
    }

    internal FilenameRenameSubmission? BuildSubmission() => CanApply ? preview : null;
}
