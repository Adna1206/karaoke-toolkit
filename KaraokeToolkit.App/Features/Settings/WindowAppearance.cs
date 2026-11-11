using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Styling;

namespace KaraokeToolkit.App.Features.Settings;

/// <summary>Applies desktop materials to existing and newly opened windows, including dialogs.</summary>
internal static class WindowAppearance
{
    private static ApplicationBackdrop current;

    public static void Initialize()
    {
        Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
        {
            window.PropertyChanged += OnWindowPropertyChanged;
            window.ActualThemeVariantChanged += (_, _) => UpdateBackground(window);
            Apply(window);
        });
    }

    public static void SetBackdrop(ApplicationBackdrop backdrop)
    {
        current = backdrop;
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            foreach (var window in desktop.Windows) Apply(window);
    }

    internal static IReadOnlyList<WindowTransparencyLevel> Levels(ApplicationBackdrop backdrop, bool windows) =>
        !windows ? [WindowTransparencyLevel.None] : backdrop switch
        {
            ApplicationBackdrop.Automatic => [WindowTransparencyLevel.Mica, WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.None],
            ApplicationBackdrop.Mica => [WindowTransparencyLevel.Mica, WindowTransparencyLevel.None],
            ApplicationBackdrop.Acrylic => [WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.None],
            _ => [WindowTransparencyLevel.None]
        };

    private static void Apply(Window window)
    {
        window.TransparencyLevelHint = Levels(current, OperatingSystem.IsWindows());
        UpdateMaterial(window);
    }

    private static void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (sender is Window window && e.Property == TopLevel.ActualTransparencyLevelProperty)
            UpdateMaterial(window);
    }

    private static void UpdateMaterial(Window window)
    {
        // Follow the material the OS actually selected, including Automatic's Acrylic fallback.
        if (window.ActualTransparencyLevel == WindowTransparencyLevel.AcrylicBlur)
            window.RequestedThemeVariant = ThemeVariant.Dark;
        else
            window.ClearValue(Window.RequestedThemeVariantProperty);
        UpdateBackground(window);
    }

    private static void UpdateBackground(Window window)
    {
        // OS policies can suppress an effect even when the requested material is supported.
        window.TransparencyBackgroundFallback = new SolidColorBrush(
            window.ActualThemeVariant == ThemeVariant.Dark ? Color.Parse("#202020") : Color.Parse("#F3F3F3"));
        if (window.ActualTransparencyLevel == WindowTransparencyLevel.None)
            window.ClearValue(Window.BackgroundProperty);
        else window.Background = Brushes.Transparent;
    }
}
