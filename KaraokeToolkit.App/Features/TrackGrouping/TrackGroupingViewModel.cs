using CommunityToolkit.Mvvm.ComponentModel;
using KaraokeToolkit.App.Shared;
using KaraokeToolkit.Core.Execution;
using KaraokeToolkit.Core.FileOperations;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace KaraokeToolkit.App.Features.TrackGrouping;

internal sealed record TrackGroupingSubmission(OperationRequest Request, TrackGroupingPlan Plan);
public sealed record TrackGroupingPreviewIssue(string Text);
public sealed record TrackGroupingNode(string Label, string Detail, IReadOnlyList<TrackGroupingNode> Children);

public sealed partial class TrackGroupingViewModel(IReadOnlyList<string> inputs) : ViewModelBase
{
    private CancellationTokenSource? previewCancellation;
    private TrackGroupingSubmission? submission;
    private bool closed;

    [ObservableProperty] public partial string? OutputFolder { get; set; } = DefaultOutput(inputs, wholeFolders: false);
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ScanFolders))]
    public partial int FolderHandling { get; set; }
    [ObservableProperty] public partial bool Recurse { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(UsesTrailingDigits)), NotifyPropertyChangedFor(nameof(UsesRanges))]
    public partial int GroupingMode { get; set; }
    [ObservableProperty] public partial decimal? TrailingDigits { get; set; } = 2;
    [ObservableProperty] public partial int MaskStyle { get; set; }
    [ObservableProperty] public partial bool CreateParents { get; set; }
    [ObservableProperty] public partial decimal? MaxParentLevels { get; set; } = 0;
    [ObservableProperty] public partial bool SkipSingleChildParents { get; set; } = true;
    [ObservableProperty] public partial decimal? RangeSize { get; set; } = 50;
    [ObservableProperty] public partial decimal? RangeStart { get; set; } = 1;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(UsesCustomRangeSeparator))]
    public partial int RangeSeparatorStyle { get; set; }
    [ObservableProperty] public partial string? CustomRangeSeparator { get; set; } = " - ";
    [ObservableProperty] public partial int IdSeparatorStyle { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ApplyLabel))]
    public partial bool Move { get; set; } = true;
    [ObservableProperty] public partial StatusKind StatusSeverity { get; set; }
    [ObservableProperty] public partial string Status { get; set; } = "";
    public PreviewProgress PreviewProgress { get; } = new();
    [ObservableProperty] public partial IReadOnlyList<TrackGroupingNode> Tree { get; set; } = [];
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasIssues))]
    public partial IReadOnlyList<TrackGroupingPreviewIssue> Issues { get; set; } = [];

    partial void OnFolderHandlingChanged(int oldValue, int newValue)
    {
        if (OutputFolder == DefaultOutput(inputs, oldValue == 1))
            OutputFolder = DefaultOutput(inputs, newValue == 1);
    }

    private static string? DefaultOutput(IReadOnlyList<string> paths, bool wholeFolders)
    {
        var folders = paths.Select(path =>
        {
            string fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            return !wholeFolders && Directory.Exists(fullPath) ? fullPath : Path.GetDirectoryName(fullPath);
        }).Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            .Take(2).ToArray();
        return folders.Length == 1 ? folders[0] : null;
    }

    public bool ScanFolders => FolderHandling == 0;
    public bool UsesTrailingDigits => GroupingMode == 0;
    public bool UsesRanges => GroupingMode == 1;
    public bool UsesCustomRangeSeparator => RangeSeparatorStyle == 2;
    public string ApplyLabel => Move ? "Move items" : "Copy items";
    public bool HasIssues => Issues.Count > 0;
    public bool CanApply => submission != null;

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(OutputFolder) or nameof(FolderHandling) or nameof(Recurse) or
            nameof(GroupingMode) or nameof(TrailingDigits) or nameof(MaskStyle) or nameof(CreateParents) or
            nameof(MaxParentLevels) or nameof(SkipSingleChildParents) or nameof(RangeSize) or nameof(RangeStart) or
            nameof(RangeSeparatorStyle) or nameof(CustomRangeSeparator) or nameof(IdSeparatorStyle) or nameof(Move))
            _ = PreviewAsync();
    }

    internal TrackGroupingSubmission? GetSubmission() => submission;

    internal void StopPreview()
    {
        closed = true;
        previewCancellation?.Cancel();
        PreviewProgress.Finish();
    }

    internal async Task PreviewAsync()
    {
        if (closed) return;
        previewCancellation?.Cancel();
        using var cancellation = new CancellationTokenSource();
        previewCancellation = cancellation;
        submission = null;
        OnPropertyChanged(nameof(CanApply));
        Tree = [];
        Issues = [];
        StatusSeverity = StatusKind.Info;
        Status = "Preparing preview…";
        var report = PreviewProgress.Begin(status => Status = status);
        try
        {
            await Task.Delay(225, cancellation.Token);
            bool hasDestination = !string.IsNullOrWhiteSpace(OutputFolder);
            if (hasDestination && !Path.IsPathFullyQualified(OutputFolder!))
                throw new ArgumentException("Enter a full destination folder path.");
            // A hypothetical destination lets the planner build the tree before the user picks a folder.
            string destination = hasDestination ? OutputFolder! : Path.Combine(Path.GetTempPath(), "Karaoke Toolkit Preview " + Guid.NewGuid());
            var options = BuildOptions();
            var request = new OperationRequest(OperationId.GroupByTrackId, inputs) { OutputFolder = destination, Grouping = options };
            var preview = await Task.Run(() =>
            {
                report("Scanning inputs", 0, 0);
                var plan = TrackGroupingPlanner.Plan(inputs, destination, hasDestination ? options : options with { Move = false },
                    cancellation.Token, (stage, completed, total) => report(stage switch
                    {
                        TrackGroupingPreviewStage.Grouping => "Grouping items",
                        TrackGroupingPreviewStage.BuildingGroups => "Building group folders",
                        TrackGroupingPreviewStage.CheckingDestinations => "Checking item destinations",
                        TrackGroupingPreviewStage.CheckingConflicts => "Checking grouping conflicts",
                        _ => "Preparing preview"
                    }, completed, total));
                report("Preparing preview", 0, 0);
                var rows = plan.Items.Select(item => (item, relativePath: Path.GetRelativePath(destination, item.Destination))).ToArray();
                var tree = BuildTree(rows, cancellation.Token);
                return (plan, tree);
            }, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();

            Tree = preview.tree;
            Issues = preview.plan.Issues.Select(issue => new TrackGroupingPreviewIssue(
                $"{(issue.IsConflict ? "Conflict" : "Skipped")} · {issue.Source} · {issue.Message}")).ToArray();
            bool conflict = preview.plan.Issues.Any(issue => issue.IsConflict);
            if (hasDestination && preview.plan.Items.Count > 0 && !conflict)
                submission = new(request, preview.plan);
            OnPropertyChanged(nameof(CanApply));
            StatusSeverity = conflict || preview.plan.Items.Count == 0 || Issues.Count > 0 ? StatusKind.Warning
                : hasDestination ? StatusKind.Success : StatusKind.Info;
            Status = conflict ? "Resolve the conflicts to continue."
                : preview.plan.Items.Count == 0 ? "No selected items can be grouped."
                : !hasDestination ? "Choose a destination to check for conflicts."
                : $"{preview.plan.Items.Count} ready · {Issues.Count} skipped";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            if (!cancellation.IsCancellationRequested)
            {
                StatusSeverity = StatusKind.Failure;
                Status = error.Message;
            }
        }
        finally
        {
            if (ReferenceEquals(previewCancellation, cancellation))
            {
                previewCancellation = null;
                PreviewProgress.Finish();
            }
        }
    }

    private TrackGroupingOptions BuildOptions() => new()
    {
        WholeFolders = FolderHandling == 1,
        Recurse = ScanFolders && Recurse,
        Move = Move,
        TrailingDigits = UsesTrailingDigits ? ReadInteger(TrailingDigits, "trailing digits") : 2,
        RangeSize = UsesRanges ? ReadInteger(RangeSize, "range size", minimum: 1) : 0,
        RangeStart = UsesRanges ? ReadInteger(RangeStart, "range start") : 1,
        CreateParents = UsesTrailingDigits && CreateParents,
        MaxParentLevels = UsesTrailingDigits && CreateParents ? ReadInteger(MaxParentLevels, "maximum parent levels") : 0,
        SkipSingleChildParents = SkipSingleChildParents,
        Mask = MaskStyle switch { 1 => "X", 2 => "#", 3 => "s", _ => "x" },
        IdSeparator = IdSeparatorStyle == 1 ? "" : "-",
        RangeSeparator = UsesRanges ? RangeSeparatorStyle switch { 1 => " to ", 2 => CustomRangeSeparator ?? "", _ => " - " } : " - "
    };

    private static int ReadInteger(decimal? value, string label, int minimum = 0) =>
        value is { } number && number >= minimum && number <= int.MaxValue && decimal.Truncate(number) == number
            ? (int)number : throw new ArgumentException($"Enter a whole number for {label}.");

    private static IReadOnlyList<TrackGroupingNode> BuildTree(
        IEnumerable<(TrackGroupingItem Item, string RelativePath)> rows, CancellationToken token)
    {
        var entries = rows.Select(row => (row.Item, Parts: row.RelativePath.Split(Path.DirectorySeparatorChar))).ToArray();
        return BuildLevel(entries, 0);

        IReadOnlyList<TrackGroupingNode> BuildLevel(IEnumerable<(TrackGroupingItem Item, string[] Parts)> level, int index)
        {
            token.ThrowIfCancellationRequested();
            var groups = level.GroupBy(row => row.Parts[index], OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal);
            var nodes = new List<TrackGroupingNode>();
            foreach (var group in groups)
            {
                var descendants = group.Where(row => row.Parts.Length > index + 1).ToArray();
                if (descendants.Length > 0)
                    nodes.Add(new($"{group.Key} ({descendants.Length})", group.Key, BuildLevel(descendants, index + 1)));
                foreach (var leaf in group.Where(row => row.Parts.Length == index + 1))
                    nodes.Add(new(leaf.Item.IsDirectory ? $"{leaf.Parts[index]} (whole folder)" : leaf.Parts[index], leaf.Item.Source, []));
            }
            return nodes;
        }
    }
}
