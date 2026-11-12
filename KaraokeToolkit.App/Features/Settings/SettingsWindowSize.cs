namespace KaraokeToolkit.App.Features.Settings;

/// <summary>Saved settings-window dimensions in logical pixels, independent of display scaling.</summary>
public sealed record SettingsWindowSize(double Width = 700, double Height = 640)
{
    public bool IsValid => double.IsFinite(Width) && double.IsFinite(Height) && Width > 0 && Height > 0;
}
