using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KaraokeToolkit.App.Shared;
using KaraokeToolkit.Core.Tagging;

namespace KaraokeToolkit.App.Features.Tagging;

public sealed record TaggingRequest(string Pattern, IReadOnlyList<TaggingPreview> Files, TaggingOptions Options);

/// <summary>Editable preview; source fingerprints remain attached when users correct parsed fields.</summary>
public partial class TaggingRow(TaggingPreview preview) : ObservableObject
{
    public TaggingPreview Preview { get; } = preview;
    public string Name => System.IO.Path.GetFileName(Preview.Path);
    public string Path => Preview.Path;
    public bool CanEdit => Preview.CanApply;
    public string Existing => Preview.Error ?? $"Current: {Preview.Existing?.Album ?? "—"} / {Preview.Existing?.Track?.ToString() ?? "—"} / {Preview.Existing?.Artist ?? "—"} / {Preview.Existing?.Title ?? "—"}";
    [ObservableProperty] private bool included = preview.CanApply;
    [ObservableProperty] private string album = preview.Tags?.Album ?? "";
    [ObservableProperty] private string track = preview.Tags?.Track?.ToString(CultureInfo.InvariantCulture) ?? "";
    [ObservableProperty] private string artist = preview.Tags?.Artist ?? "";
    [ObservableProperty] private string title = preview.Tags?.Title ?? "";
    internal bool HasEdits => Included != Preview.CanApply || Album != (Preview.Tags?.Album ?? "")
        || Track != (Preview.Tags?.Track?.ToString(CultureInfo.InvariantCulture) ?? "")
        || Artist != (Preview.Tags?.Artist ?? "") || Title != (Preview.Tags?.Title ?? "");
    public TaggingPreview ToPreview()
    {
        if (!Included || !CanEdit) return Preview with { Tags = null, Error = Preview.Error ?? "Excluded in preview." };
        uint? number = null;
        if (!string.IsNullOrWhiteSpace(Track))
        {
            if (!uint.TryParse(Track, NumberStyles.None, CultureInfo.InvariantCulture, out uint parsed))
                throw new FormatException($"{Name}: Track must be a whole number of 0 or greater.");
            number = parsed;
        }
        string? Value(string input, string? original) => string.IsNullOrWhiteSpace(input) && original == null ? null : input.Trim();
        return Preview with { Tags = new(Value(Album, Preview.Tags!.Album), number, Value(Artist, Preview.Tags.Artist), Value(Title, Preview.Tags.Title)) };
    }
}

public partial class TaggingViewModel(IReadOnlyList<string> inputs, bool recurse, IReadOnlyList<string> patterns, string initialPattern) : ViewModelBase
{
    private readonly KaraokeTaggingService service = new();
    private readonly string originalPattern = initialPattern;
    private IReadOnlyList<TaggingPreview>? sources;
    private string? previewPattern;
    private bool changingSelection;
    public IReadOnlyList<string> Patterns { get; } = patterns;
    public ObservableCollection<TaggingRow> Rows { get; } = [];
    public PreviewProgress PreviewProgress { get; } = new();
    internal string RecoveryNotice { get; set; } = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanApply))] private string pattern = initialPattern;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanApply)), NotifyPropertyChangedFor(nameof(CanPreview)), NotifyCanExecuteChangedFor(nameof(PreviewCommand))]
    [NotifyCanExecuteChangedFor(nameof(SelectAllCommand)), NotifyCanExecuteChangedFor(nameof(DeselectAllCommand))]
    private bool busy;
    [ObservableProperty] private StatusKind statusSeverity;
    [ObservableProperty] private string status = "Preview the filename tags before applying them.";
    [ObservableProperty] private bool fillEmptyOnly;
    [ObservableProperty] private bool clearComments = true;
    [ObservableProperty] private bool clearOtherDescriptiveTags = true;
    public bool CanPreview => !Busy;
    public bool CanApply => !Busy && previewPattern == Pattern && Rows.Any(r => r.CanEdit && r.Included);
    internal bool HasEdits => Pattern != originalPattern || FillEmptyOnly || !ClearComments
        || !ClearOtherDescriptiveTags || Rows.Any(row => row.HasEdits);

    partial void OnPatternChanged(string value) => UpdatePreview();

    [RelayCommand(CanExecute = nameof(CanPreview))]
    public async Task PreviewAsync(CancellationToken token = default)
    {
        if (Busy) return;
        Busy = true;
        sources = null;
        previewPattern = null;
        StatusSeverity = StatusKind.Info;
        Status = "Scanning inputs for media…";
        var report = PreviewProgress.Begin(status => Status = status);
        try
        {
            sources = await service.ReadPreviewAsync(inputs, recurse, token,
                (completed, total) => report("Reading media tags", completed, total));
            UpdatePreview();
        }
        catch (OperationCanceledException) { Status = "Preview cancelled."; }
        catch (Exception error) { StatusSeverity = StatusKind.Failure; Status = error.Message; }
        finally
        {
            PreviewProgress.Finish();
            Busy = false;
            NotifyPreviewChanged();
        }
    }

    private void UpdatePreview()
    {
        if (sources == null) return;
        previewPattern = null;
        try
        {
            var result = KaraokeTaggingService.PlanPreview(sources, Pattern).ToArray();
            foreach (var row in Rows)
                row.PropertyChanged -= OnRowPropertyChanged;
            Rows.Clear();
            foreach (var preview in result)
            {
                var row = new TaggingRow(preview);
                row.PropertyChanged += OnRowPropertyChanged;
                Rows.Add(row);
            }
            previewPattern = Pattern;
            int ready = Rows.Count(row => row.CanEdit);
            int needsAttention = Rows.Count - ready;
            StatusSeverity = ready == 0 || needsAttention > 0 ? StatusKind.Warning : StatusKind.Success;
            Status = $"{ready} ready; {needsAttention} need attention. Review all fields, especially names containing your separators.";
            if (RecoveryNotice.Length > 0)
            {
                Status = $"{RecoveryNotice} {Status}";
                RecoveryNotice = "";
            }
        }
        catch (Exception error) when (error is ArgumentException or FormatException)
        {
            foreach (var row in Rows) row.PropertyChanged -= OnRowPropertyChanged;
            Rows.Clear();
            StatusSeverity = StatusKind.Failure;
            Status = error.Message;
        }
        finally
        {
            NotifyPreviewChanged();
        }
    }

    private void NotifyPreviewChanged()
    {
        OnPropertyChanged(nameof(CanApply));
        SelectAllCommand.NotifyCanExecuteChanged();
        DeselectAllCommand.NotifyCanExecuteChanged();
    }
    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (!changingSelection && args.PropertyName == nameof(TaggingRow.Included))
            OnPropertyChanged(nameof(CanApply));
    }

    private bool CanChangeSelection => !Busy && Rows.Any(row => row.CanEdit);

    [RelayCommand(CanExecute = nameof(CanChangeSelection))]
    private void SelectAll() => SetIncluded(true);

    [RelayCommand(CanExecute = nameof(CanChangeSelection))]
    private void DeselectAll() => SetIncluded(false);

    private void SetIncluded(bool included)
    {
        changingSelection = true;
        try
        {
            foreach (var row in Rows.Where(row => row.CanEdit))
                row.Included = included;
        }
        finally
        {
            changingSelection = false;
            OnPropertyChanged(nameof(CanApply));
        }
    }

    public TaggingRequest? BuildRequest()
    {
        if (!CanApply)
        {
            StatusSeverity = StatusKind.Warning;
            Status = Busy || previewPattern != Pattern
                ? "Refresh the preview before applying tags."
                : "Select at least one file to tag.";
            return null;
        }
        try
        {
            var files = Rows.Select(r => r.ToPreview()).ToArray();
            return new(Pattern, files, new(FillEmptyOnly, ClearComments, ClearOtherDescriptiveTags));
        }
        catch (FormatException error) { StatusSeverity = StatusKind.Warning; Status = error.Message; return null; }
    }
}
