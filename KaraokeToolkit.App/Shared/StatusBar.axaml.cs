using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace KaraokeToolkit.App.Shared;

public enum StatusKind { Info, Success, Warning, Failure }

public sealed partial class StatusBar : UserControl
{
    public static readonly StyledProperty<string?> MessageProperty =
        AvaloniaProperty.Register<StatusBar, string?>(nameof(Message));
    public static readonly StyledProperty<StatusKind> KindProperty =
        AvaloniaProperty.Register<StatusBar, StatusKind>(nameof(Kind));
    private const double ScrollSpeed = 30;
    private const double PauseSeconds = 1.5;
    private readonly Stopwatch scrollClock = new();
    private readonly DispatcherTimer scrollTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };

    public StatusBar()
    {
        InitializeComponent();
        scrollTimer.Tick += (_, _) =>
        {
            double overflow = HorizontalOverflow;
            double cycle = 2 * PauseSeconds + overflow / ScrollSpeed;
            double elapsed = scrollClock.Elapsed.TotalSeconds % cycle;
            MessageScroll.Offset = new Vector(Math.Clamp((elapsed - PauseSeconds) * ScrollSpeed, 0, overflow), 0);
        };
    }

    public string? Message { get => GetValue(MessageProperty); set => SetValue(MessageProperty, value); }
    public StatusKind Kind { get => GetValue(KindProperty); set => SetValue(KindProperty, value); }
    private double HorizontalOverflow => Math.Max(0, MessageScroll.Extent.Width - MessageScroll.Viewport.Width);

    private void OnMessageScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (e.ExtentDelta.X != 0 || e.ViewportDelta.X != 0)
            ResetScroll();
    }

    private void ResetScroll()
    {
        if (MessageScroll is not { } viewer)
            return;

        scrollTimer.Stop();
        scrollClock.Reset();
        viewer.Offset = default;
        if (this.IsAttachedToVisualTree() && viewer.Viewport.Width > 0 &&
            !string.IsNullOrEmpty(Message) && HorizontalOverflow > 0)
        {
            scrollClock.Start();
            scrollTimer.Start();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ResetScroll();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        scrollTimer.Stop();
        scrollClock.Reset();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MessageProperty)
            ResetScroll();

        if (change.Property != KindProperty)
            return;

        PseudoClasses.Set(":success", Kind == StatusKind.Success);
        PseudoClasses.Set(":warning", Kind == StatusKind.Warning);
        PseudoClasses.Set(":failure", Kind == StatusKind.Failure);
    }
}
