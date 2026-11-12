using System;
using KaraokeToolkit.Core.Settings;

namespace KaraokeToolkit.App.Features.Settings;

internal sealed record SettingsWindowLayout(SettingsWindowSize Size, double MinWidth, double MinHeight, double MaxWidth, double MaxHeight)
{
    /// <summary>Fits a client area to the current display, allowing room for borders and the title bar.</summary>
    internal static SettingsWindowLayout Fit(SettingsWindowSize requested, double workingWidth, double workingHeight)
    {
        if (!requested.IsValid) requested = new();
        if (!double.IsFinite(workingWidth) || !double.IsFinite(workingHeight) || workingWidth <= 0 || workingHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(workingWidth));
        double maxWidth = Math.Max(1, workingWidth - 32), maxHeight = Math.Max(1, workingHeight - 72);
        double minWidth = Math.Min(660, maxWidth), minHeight = Math.Min(540, maxHeight);
        return new(new(Math.Clamp(requested.Width, minWidth, maxWidth), Math.Clamp(requested.Height, minHeight, maxHeight)),
            minWidth, minHeight, maxWidth, maxHeight);
    }
}
