using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
    private string? previewPattern;
    public IReadOnlyList<string> Patterns { get; } = patterns;
    public ObservableCollection<TaggingRow> Rows { get; } = [];
    [ObservableProperty] private string recoveryNotice = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanApply))] private string pattern = initialPattern;
    [ObservableProperty] private string? selectedPattern = initialPattern;
    partial void OnSelectedPatternChanged(string? value) { if (value != null) Pattern = value; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanApply)), NotifyPropertyChangedFor(nameof(CanPreview)), NotifyCanExecuteChangedFor(nameof(PreviewCommand))] private bool busy;
    [ObservableProperty] private string status = "Preview the filename tags before applying them.";
    [ObservableProperty] private bool fillEmptyOnly;
    [ObservableProperty] private bool clearComments = true;
    [ObservableProperty] private bool clearOtherDescriptiveTags = true;
    public bool CanPreview => !Busy;
    public bool CanApply => !Busy && previewPattern == Pattern && Rows.Any(r => r.CanEdit);
    [RelayCommand(CanExecute = nameof(CanPreview))]
    public async Task PreviewAsync(CancellationToken token = default)
    {
        Busy = true;
        Status = "Reading media tags…";
        try
        {
            string requested = Pattern;
            var result = await service.PreviewAsync(inputs, requested, recurse, token);
            Rows.Clear();
            foreach (var row in result) Rows.Add(new(row));
            previewPattern = requested;
            Status = $"{Rows.Count(r => r.CanEdit)} ready; {Rows.Count(r => !r.CanEdit)} need attention. Review all fields, especially names containing your separators.";
        }
        catch (OperationCanceledException) { Status = "Preview cancelled."; }
        catch (Exception error) { previewPattern = null; Status = error.Message; }
        finally { Busy = false; OnPropertyChanged(nameof(CanApply)); }
    }
    public TaggingRequest? BuildRequest()
    {
        if (!CanApply) { Status = "Refresh the preview before applying tags."; return null; }
        try
        {
            var files = Rows.Select(r => r.ToPreview()).ToArray();
            if (!files.Any(p => p.CanApply)) { Status = "Select at least one file to tag."; return null; }
            return new(Pattern, files, new(FillEmptyOnly, ClearComments, ClearOtherDescriptiveTags));
        }
        catch (FormatException error) { Status = error.Message; return null; }
    }
}
