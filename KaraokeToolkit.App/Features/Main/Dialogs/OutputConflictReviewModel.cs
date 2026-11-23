using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using KaraokeToolkit.Core.Conversion;

namespace KaraokeToolkit.App.Features.Main.Dialogs;

public sealed partial class OutputConflictReviewRow(OutputConflict conflict) : ObservableObject
{
    public string Source => conflict.Source;
    public string Name => Path.GetFileName(Source);
    public string ExistingOutputs { get; } = string.Join("\n", conflict.ExistingFiles);
    public string ReplaceLabel => $"Replace outputs for {Name}";

    [ObservableProperty]
    public partial bool Replace { get; set; }
}

public sealed class OutputConflictReviewModel : ObservableObject
{
    public IReadOnlyList<OutputConflictReviewRow> Rows { get; }
    private int replaceCount;
    public bool HasReplacements => replaceCount > 0;
    public string Summary => $"{replaceCount} replace · {Rows.Count - replaceCount} skip";

    public OutputConflictReviewModel(IReadOnlyList<OutputConflict> conflicts)
    {
        Rows = conflicts.Select(conflict => new OutputConflictReviewRow(conflict)).ToArray();
        foreach (var row in Rows)
            row.PropertyChanged += RowChanged;
    }

    public void SetAll(bool replace)
    {
        foreach (var row in Rows)
            row.Replace = replace;
    }

    public IReadOnlyList<ConflictDecision> GetChoices() =>
        Rows.Select(row => row.Replace ? ConflictDecision.Replace : ConflictDecision.Skip).ToArray();

    private void RowChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(OutputConflictReviewRow.Replace) || sender is not OutputConflictReviewRow row) return;
        replaceCount += row.Replace ? 1 : -1;
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(HasReplacements));
    }
}
