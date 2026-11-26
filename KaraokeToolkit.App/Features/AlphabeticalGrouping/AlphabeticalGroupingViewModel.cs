using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Data.Converters;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KaraokeToolkit.App.Shared;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.FileOperations;

namespace KaraokeToolkit.App.Features.AlphabeticalGrouping;

internal sealed record AlphabeticalGroupingSubmission(AlphabeticalOrganizeScope Scope, bool IgnoreLeadingThe,
    string? TrackIdSeparator, ConversionResult Plan);

public sealed record AlphabeticalPreviewRow(ConversionFileResult File)
{
    public string SourceName => Path.GetFileName(File.Source);
    public string Target => File.Outputs[0];
    public string TargetName => Path.GetRelativePath(Path.GetDirectoryName(File.Source)!, Target);
}

public sealed partial class AlphabeticalGroupingViewModel(IReadOnlyList<string> inputs, bool ignoreLeadingThe,
    AlphabeticalOrganizer organizer) : ViewModelBase
{
    private const string DefaultTrackIdSeparator = " - ";
    private readonly bool initialIgnoreLeadingThe = ignoreLeadingThe;
    private readonly PreviewWork<AlphabeticalOrganizer.PreviewSnapshot> work = new();
    private AlphabeticalGroupingSubmission? preview;

    public PreviewProgress PreviewProgress { get; } = new();
    public IReadOnlyList<string> TrackIdSeparators { get; } = [DefaultTrackIdSeparator, ". ", " – ", "_", " "];
    public static FuncValueConverter<string, string> TrackIdSeparatorLabel { get; } = new(separator =>
        separator == " " ? "Whitespace (spaces or tabs)" : $"“{separator}”");

    [ObservableProperty, NotifyPropertyChangedFor(nameof(OrganizeFiles), nameof(ShowTrackIdSeparator), nameof(CanApply))]
    public partial int ScopeIndex { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanApply))]
    public partial bool IgnoreLeadingThe { get; set; } = ignoreLeadingThe;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowTrackIdSeparator))]
    public partial bool SkipLeadingTrackId { get; set; }
    [ObservableProperty]
    public partial string? TrackIdSeparator { get; set; } = DefaultTrackIdSeparator;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanApply), nameof(CanPreview))]
    public partial bool IsBusy { get; private set; }
    [ObservableProperty]
    public partial IReadOnlyList<AlphabeticalPreviewRow> Moves { get; private set; } = [];
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasIssues))]
    public partial IReadOnlyList<AlphabeticalPreviewRow> Issues { get; private set; } = [];
    [ObservableProperty]
    public partial string Status { get; private set; } = "Refresh to review the destination paths.";
    [ObservableProperty]
    public partial StatusKind StatusSeverity { get; private set; }

    public bool OrganizeFiles => ScopeIndex == (int)AlphabeticalOrganizeScope.Files;
    public bool ShowTrackIdSeparator => OrganizeFiles && SkipLeadingTrackId;
    public bool HasIssues => Issues.Count > 0;
    public bool CanPreview => !IsBusy;
    public bool CanApply => !IsBusy && preview != null && Moves.Count > 0;
    internal bool HasEdits => !OrganizeFiles || IgnoreLeadingThe != initialIgnoreLeadingThe || SkipLeadingTrackId;
    internal Task PendingPreview { get; private set; } = Task.CompletedTask;

    partial void OnScopeIndexChanged(int value) => UpdatePreview();
    partial void OnIgnoreLeadingTheChanged(bool value) => UpdatePreview();
    partial void OnSkipLeadingTrackIdChanged(bool value) => UpdatePreview();
    partial void OnTrackIdSeparatorChanged(string? value)
    {
        if (ShowTrackIdSeparator) UpdatePreview();
    }

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
        var scope = (AlphabeticalOrganizeScope)ScopeIndex;
        bool ignoreArticle = IgnoreLeadingThe;
        string? separator = ShowTrackIdSeparator ? TrackIdSeparator ?? "" : null;
        preview = null;
        IsBusy = true;
        Status = "Updating preview…";
        StatusSeverity = StatusKind.Info;
        PreviewProgress.Begin(status => Status = status);
        try
        {
            if (!refresh) await Task.Delay(PreviewWork<AlphabeticalOrganizer.PreviewSnapshot>.EditDelay, cancellation);
            var scan = work.ReadAsync(scanToken => organizer.ReadPreviewAsync(inputs, scanToken), cancellation);
            if (!scan.IsCompleted) Status = "Finding items to group…";
            var snapshot = await scan;
            var result = await Task.Run(() =>
            {
                var plan = snapshot.Plan(scope, ignoreArticle, separator, cancellation);
                var moves = new List<AlphabeticalPreviewRow>();
                var issues = new List<AlphabeticalPreviewRow>();
                foreach (var file in plan.Files)
                {
                    cancellation.ThrowIfCancellationRequested();
                    (file.Status == ConversionStatus.Succeeded ? moves : issues).Add(new(file));
                }
                return (plan, moves, issues);
            }, cancellation);
            cancellation.ThrowIfCancellationRequested();
            preview = new(scope, ignoreArticle, separator, result.plan);
            Moves = result.moves;
            Issues = result.issues;
            Status = $"{Moves.Count} ready · {Issues.Count} skipped";
            StatusSeverity = Moves.Count == 0 || HasIssues ? StatusKind.Warning : StatusKind.Success;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException)
        {
            if (!cancellation.IsCancellationRequested)
            {
                Moves = [];
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

    internal AlphabeticalGroupingSubmission? BuildSubmission() => CanApply ? preview : null;
}
