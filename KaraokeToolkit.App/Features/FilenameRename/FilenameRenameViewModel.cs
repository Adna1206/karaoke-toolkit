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
    private readonly FilenameRenameService service = new();
    private readonly bool initialRecurse = recurse;
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
    public partial string Status { get; private set; } = "Choose the source and destination patterns, then refresh the preview.";
    [ObservableProperty]
    public partial StatusKind StatusSeverity { get; private set; }

    public bool CanPreview => !IsBusy;
    public bool HasIssues => Issues.Count > 0;
    public bool CanApply => !IsBusy && preview is { Plan.CanApply: true } && preview.Request == Request;
    internal bool HasEdits => SourcePattern != DefaultPattern || DestinationPattern != DefaultPattern ||
        RemoveDiscWhitespace != FilenameRenameRequest.DefaultRemoveDiscWhitespace || !UppercaseDisc || IncludeSubfolders != initialRecurse;
    private FilenameRenameRequest Request => new(inputs, SourcePattern, DestinationPattern,
        RemoveDiscWhitespace, IncludeSubfolders, UppercaseDisc);

    partial void OnSourcePatternChanged(string value) => InvalidatePreview();
    partial void OnDestinationPatternChanged(string value) => InvalidatePreview();
    partial void OnRemoveDiscWhitespaceChanged(bool value) => InvalidatePreview();
    partial void OnUppercaseDiscChanged(bool value) => InvalidatePreview();
    partial void OnIncludeSubfoldersChanged(bool value) => InvalidatePreview();

    private void InvalidatePreview()
    {
        preview = null;
        Changes = [];
        Issues = [];
        Status = "Refresh to review the new filenames.";
        StatusSeverity = StatusKind.Info;
        OnPropertyChanged(nameof(CanApply));
    }

    [RelayCommand]
    internal async Task RefreshAsync(CancellationToken token = default)
    {
        if (IsBusy) return;
        IsBusy = true;
        preview = null;
        Changes = [];
        Issues = [];
        Status = "Checking filenames…";
        StatusSeverity = StatusKind.Info;
        PreviewProgress.Begin(status => Status = status);
        try
        {
            var request = Request;
            var plan = await service.PreviewAsync(request, token);
            preview = new(request, plan);
            Changes = plan.Items.Where(item => item.Destination != null && !item.IsConflict)
                .Select(item => new FilenameRenamePreviewRow(item)).ToArray();
            Issues = plan.Items.Where(item => item.Destination == null || item.IsConflict)
                .OrderByDescending(item => item.IsConflict).Select(item => new FilenameRenamePreviewRow(item)).ToArray();
            int ready = Changes.Count;
            int conflicts = Issues.Count(row => row.Item.IsConflict);
            int skipped = Issues.Count - conflicts;
            Status = $"{ready} ready · {skipped} skipped · {conflicts} {(conflicts == 1 ? "conflict" : "conflicts")}";
            StatusSeverity = conflicts > 0 ? StatusKind.Failure : ready == 0 || skipped > 0 ? StatusKind.Warning : StatusKind.Success;
        }
        catch (OperationCanceledException)
        {
            Status = "Preview cancelled.";
        }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException)
        {
            Status = error.Message;
            StatusSeverity = StatusKind.Failure;
        }
        finally
        {
            PreviewProgress.Finish();
            IsBusy = false;
        }
    }

    internal FilenameRenameSubmission? BuildSubmission() => CanApply ? preview : null;
}
