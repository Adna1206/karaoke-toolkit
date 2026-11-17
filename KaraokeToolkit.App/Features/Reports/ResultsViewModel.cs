using KaraokeToolkit.App.Shared;
using KaraokeToolkit.App.Features.Main;
using KaraokeToolkit.Core.Reporting;
using KaraokeToolkit.Core.Validation;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using KaraokeToolkit.Core;

namespace KaraokeToolkit.App.Features.Reports;

public sealed record ReportRow(ReportEntry Entry, ValidationFinding? Finding)
{
    public string Source => Entry.Source;
    public string Status => Entry.Status;
    public string Message => Entry.Message;
    public string Outputs => string.Join("\n", Entry.Outputs);
    public bool CanMove => Finding?.CanMove == true && Entry.Status == Finding.Status.ToString();
}

public partial class ResultsViewModel(MainViewModel owner) : ViewModelBase
{
    public CompletionReport Report => owner.LastReport!;
    public string Title => Report.Operation;
    public string HeaderSummary => Report.Header.ToText() + Summary;
    public string Summary => Report.Entries.Count == 0 ? "No results recorded." : string.Join(" · ", Report.Entries.GroupBy(e => e.Status).Select(g => $"{g.Count()} {g.Key}"));
    public bool CanRetry => owner.CanRetry && !IsBusy;
    public bool IsValidation => owner.ValidationFindings.Count > 0;
    public bool CanEdit => !IsBusy;
    public IReadOnlyList<string> Filters { get; } = ["All", "Needs attention", "Succeeded", "Skipped", "Failed", "Valid", "Damaged", "Incomplete", "Ambiguous", "Unsupported", "Unreadable", "Moved", "Restored"];
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Rows))] public partial string Filter { get; set; } = "All";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanEdit)), NotifyPropertyChangedFor(nameof(CanRetry))]
    public partial bool IsBusy
    {
        get; set;
    }
    [ObservableProperty] public partial StatusKind MessageSeverity { get; set; }
    [ObservableProperty] public partial string Message { get; set; } = "Select a row to open its output folder. Ctrl/Shift selects multiple validation results.";
    public IReadOnlyList<ReportRow> Rows => Report.Entries.Where(e => Filter == "All" || (Filter == "Needs attention" ? e.Status is not ("Succeeded" or "Valid" or "Moved" or "Restored") : e.Status == Filter))
        .Select(e => new ReportRow(e, owner.ValidationFindings.FirstOrDefault(f => f.Source == e.Source))).ToArray();
    public void Replace(IEnumerable<ReportEntry> changes)
    {
        var updated = changes.ToDictionary(e => e.Source);
        owner.LastReport = Report with
        {
            Entries = Report.Entries.Select(e => updated.GetValueOrDefault(e.Source, e)).ToArray()
        };
        OnPropertyChanged(nameof(Rows));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(HeaderSummary));
    }
}
