using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KaraokeToolkit.App.Shared;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.FileOperations;

namespace KaraokeToolkit.App.Features.AlphabeticalGrouping;

internal sealed record AlphabeticalGroupingSubmission(AlphabeticalOrganizeScope Scope, bool IgnoreLeadingThe, ConversionResult Plan);

public sealed record AlphabeticalPreviewRow(ConversionFileResult File)
{
    public string SourceName => Path.GetFileName(File.Source);
    public string Target => File.Outputs[0];
    public string TargetName => Path.GetRelativePath(Path.GetDirectoryName(File.Source)!, Target);
}

public sealed partial class AlphabeticalGroupingViewModel(IReadOnlyList<string> inputs, bool ignoreLeadingThe,
    AlphabeticalOrganizer organizer) : ViewModelBase
{
    private readonly bool initialIgnoreLeadingThe = ignoreLeadingThe;
    private AlphabeticalGroupingSubmission? preview;

    public PreviewProgress PreviewProgress { get; } = new();

    [ObservableProperty, NotifyPropertyChangedFor(nameof(OrganizeFiles), nameof(CanApply))]
    public partial int ScopeIndex { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanApply))]
    public partial bool IgnoreLeadingThe { get; set; } = ignoreLeadingThe;
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
    public bool HasIssues => Issues.Count > 0;
    public bool CanPreview => !IsBusy;
    public bool CanApply => !IsBusy && preview != null && Moves.Count > 0;
    internal bool HasEdits => !OrganizeFiles || IgnoreLeadingThe != initialIgnoreLeadingThe;

    partial void OnScopeIndexChanged(int value) => InvalidatePreview();
    partial void OnIgnoreLeadingTheChanged(bool value) => InvalidatePreview();

    private void InvalidatePreview()
    {
        preview = null;
        Moves = [];
        Issues = [];
        Status = "Refresh to review the destination paths.";
        StatusSeverity = StatusKind.Info;
    }

    [RelayCommand]
    internal async Task RefreshAsync(CancellationToken token = default)
    {
        if (IsBusy) return;
        IsBusy = true;
        InvalidatePreview();
        Status = "Finding items to group…";
        PreviewProgress.Begin(status => Status = status);
        try
        {
            var scope = (AlphabeticalOrganizeScope)ScopeIndex;
            bool ignoreArticle = IgnoreLeadingThe;
            var plan = await organizer.PreviewAsync(inputs, scope, token, ignoreArticle);
            if (scope != (AlphabeticalOrganizeScope)ScopeIndex || ignoreArticle != IgnoreLeadingThe) return;
            preview = new(scope, ignoreArticle, plan);
            Moves = plan.Files.Where(file => file.Status == ConversionStatus.Succeeded)
                .Select(file => new AlphabeticalPreviewRow(file)).ToArray();
            Issues = plan.Files.Where(file => file.Status != ConversionStatus.Succeeded)
                .Select(file => new AlphabeticalPreviewRow(file)).ToArray();
            Status = $"{Moves.Count} ready · {Issues.Count} skipped";
            StatusSeverity = Moves.Count == 0 || HasIssues ? StatusKind.Warning : StatusKind.Success;
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

    internal AlphabeticalGroupingSubmission? BuildSubmission() => CanApply ? preview : null;
}
