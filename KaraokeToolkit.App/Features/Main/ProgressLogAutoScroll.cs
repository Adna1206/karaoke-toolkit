using System;
using System.Collections.Specialized;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace KaraokeToolkit.App.Features.Main;

/// <summary>Follows new progress entries until the user scrolls away from the bottom.</summary>
internal sealed class ProgressLogAutoScroll : IDisposable
{
    private readonly ListBox list;
    private readonly LogScrollState state = new();
    private ScrollViewer? viewer;
    private MainViewModel? model;
    private bool queued;
    private bool disposed;

    public ProgressLogAutoScroll(ListBox list)
    {
        this.list = list;
        list.TemplateApplied += OnTemplateApplied;
        list.Loaded += OnLoaded;
        list.DataContextChanged += OnDataContextChanged;
        BindModel();
    }

    private void OnTemplateApplied(object? sender, Avalonia.Controls.Primitives.TemplateAppliedEventArgs e) => AttachViewer();
    private void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => AttachViewer();
    private void OnDataContextChanged(object? sender, EventArgs e) => BindModel();

    private void BindModel()
    {
        if (model != null) model.Activity.CollectionChanged -= OnEntriesChanged;
        model = list.DataContext as MainViewModel;
        if (model != null) model.Activity.CollectionChanged += OnEntriesChanged;
        state.Reset();
        ScheduleScroll();
    }

    private void AttachViewer()
    {
        if (viewer != null) viewer.ScrollChanged -= OnScrolled;
        viewer = list.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (viewer != null) viewer.ScrollChanged += OnScrolled;
        ScheduleScroll();
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // A new run clears the log and starts following again.
        if (e.Action == NotifyCollectionChangedAction.Reset) state.Reset();
        ScheduleScroll();
    }

    private void OnScrolled(object? sender, ScrollChangedEventArgs e)
    {
        if (viewer == null) return;
        // Adding rows or wrapping text can increase the extent without a user scroll.
        // Do not interpret that growing gap as a request to stop following.
        bool layoutChanged = e.ExtentDelta.Y != 0 || e.ViewportDelta.Y != 0;
        state.Observe(viewer.Offset.Y, MaximumOffset, layoutChanged);
        if (layoutChanged) ScheduleScroll();
    }

    private double MaximumOffset => viewer == null ? 0 : Math.Max(0, viewer.Extent.Height - viewer.Viewport.Height);

    private void ScheduleScroll()
    {
        if (disposed || queued || !state.Following) return;
        queued = true;
        // Wait for new rows to be measured; coalesce a burst of progress events into one scroll.
        Dispatcher.UIThread.Post(() =>
        {
            queued = false;
            if (disposed || !state.Following || viewer == null) return;
            if (MaximumOffset - viewer.Offset.Y > LogScrollState.BottomTolerance) viewer.ScrollToEnd();
        }, DispatcherPriority.Background);
    }

    public void Dispose()
    {
        disposed = true;
        list.TemplateApplied -= OnTemplateApplied;
        list.Loaded -= OnLoaded;
        list.DataContextChanged -= OnDataContextChanged;
        if (model != null) model.Activity.CollectionChanged -= OnEntriesChanged;
        if (viewer != null) viewer.ScrollChanged -= OnScrolled;
    }
}

/// <summary>Tracks user intent independently of layout changes and queued UI updates.</summary>
internal sealed class LogScrollState
{
    internal const double BottomTolerance = 2;
    internal bool Following { get; private set; } = true;
    internal void Reset() => Following = true;
    internal void Observe(double offset, double maximumOffset, bool layoutChanged)
    {
        if (!layoutChanged) Following = maximumOffset - offset <= BottomTolerance;
    }
}
