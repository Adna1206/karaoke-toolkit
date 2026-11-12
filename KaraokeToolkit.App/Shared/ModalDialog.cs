using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Media;
using Avalonia.Threading;

namespace KaraokeToolkit.App.Shared;

/// <summary>Shows modal windows or native pickers with a smoke overlay on their owner.</summary>
internal static class ModalDialog
{
    private static readonly ConditionalWeakTable<Window, ModalOverlay> overlays = new();

    public static Task ShowAsync(Window dialog, Window owner) =>
        RunAsync(owner, () => dialog.ShowDialog(owner));

    public static Task<T> ShowAsync<T>(Window dialog, Window owner) =>
        RunAsync(owner, () => dialog.ShowDialog<T>(owner));

    public static Task RunAsync(Window owner, Func<Task> show)
    {
        Dispatcher.UIThread.VerifyAccess();
        return overlays.GetValue(owner, CreateOverlay).RunAsync(show);
    }

    public static Task<T> RunAsync<T>(Window owner, Func<Task<T>> show)
    {
        Dispatcher.UIThread.VerifyAccess();
        return overlays.GetValue(owner, CreateOverlay).RunAsync(show);
    }

    private static ModalOverlay CreateOverlay(Window owner)
    {
        OverlayLayer? layer = null;
        Border? smoke = null;
        return new ModalOverlay(
            show: () =>
            {
                layer = OverlayLayer.GetOverlayLayer(owner);
                // Custom templates may omit the overlay layer. ShowDialog still provides modality.
                if (layer == null) return;
                smoke = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(64, 0, 0, 0)),
                    IsHitTestVisible = false, Focusable = false, ZIndex = int.MaxValue
                };
                // OverlayLayer is a Canvas: explicit bindings keep the overlay covering a resized owner.
                smoke.Bind(Control.WidthProperty, new Binding("Bounds.Width") { Source = layer });
                smoke.Bind(Control.HeightProperty, new Binding("Bounds.Height") { Source = layer });
                layer.Children.Add(smoke);
            },
            hide: () =>
            {
                if (smoke != null) layer?.Children.Remove(smoke);
                smoke = null;
                layer = null;
            });
    }
}

/// <summary>Balances asynchronous modal lifetimes; overlapping dialogs on one owner share one overlay.</summary>
internal sealed class ModalOverlay(Action show, Action hide)
{
    private int active;

    public async Task RunAsync(Func<Task> action) =>
        await RunAsync(async () => { await action(); return true; });

    public async Task<T> RunAsync<T>(Func<Task<T>> action)
    {
        active++;
        try
        {
            if (active == 1) show();
            return await action();
        }
        finally
        {
            if (--active == 0) hide();
        }
    }
}
