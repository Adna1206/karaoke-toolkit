using Avalonia;

namespace KaraokeToolkit.App.Shared;

public static class WindowSpacing
{
    public static Thickness ContentInset { get; } = new(24, 14);
    public static Thickness DialogFooterMargin { get; } = new(-ContentInset.Left, 0, -ContentInset.Right, 0);
    public static Thickness DialogFooterPadding { get; } = new(ContentInset.Left, ContentInset.Top, ContentInset.Right, 0);
    public const double FooterButtonGap = 10;
    public const double StatusProgressGap = 8;
    public const double StatusProgressWidth = 96;
}
