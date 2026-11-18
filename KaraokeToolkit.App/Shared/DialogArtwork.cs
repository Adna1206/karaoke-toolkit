using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace KaraokeToolkit.App.Shared;

public static class DialogArtwork
{
    public const string Update = "M 8,3 H 16 V 11 H 21 L 12,20 L 3,11 H 8 Z M 4,22 H 20 V 24 H 4 Z";
    public const string DiscRip = "M 10,2 A 10,10 0 1 0 18,18 H 15 A 8,8 0 1 1 17.75,10 H 20 A 10,10 0 0 0 10,2 Z M 10,8 A 4,4 0 1 0 10,16 A 4,4 0 1 0 10,8 Z M 10,10 A 2,2 0 1 1 10,14 A 2,2 0 1 1 10,10 Z M 20,9 L 25,14 L 20,19 V 15 H 16 V 13 H 20 Z";
    public const string Help = "M 12,1 A 11,11 0 1 0 12,23 A 11,11 0 1 0 12,1 Z M 12,3 A 9,9 0 1 1 12,21 A 9,9 0 1 1 12,3 Z M 8,9 A 4,4 0 1 1 14,12.5 Q 13,13 13,15 H 11 Q 11,12 13,11 A 2,2 0 1 0 10,9 Z M 11,17 H 13 V 19 H 11 Z";
    public const string Settings = "M 2,5 H 6 V 2 H 9 V 5 H 22 V 7 H 9 V 10 H 6 V 7 H 2 Z M 2,12 H 15 V 9 H 18 V 12 H 22 V 14 H 18 V 17 H 15 V 14 H 2 Z M 2,19 H 8 V 16 H 11 V 19 H 22 V 21 H 11 V 24 H 8 V 21 H 2 Z";
    public const string Report = "M 4,2 H 15 L 20,7 V 22 H 4 Z M 6,4 V 20 H 18 V 8 H 14 V 4 Z M 8,10 H 16 V 12 H 8 Z M 8,15 H 16 V 17 H 8 Z";
    public const string Exit = "M 3,3 H 13 V 5 H 5 V 19 H 13 V 21 H 3 Z M 16,6 L 22,12 L 16,18 L 14.5,16.5 L 18,13 H 8 V 11 H 18 L 14.5,7.5 Z";
    public const string Recipe = "M 3,2 H 9 V 8 H 3 Z M 15,9 H 21 V 15 H 15 Z M 3,16 H 9 V 22 H 3 Z M 10,4 H 19 V 8 H 17 V 6 H 10 Z M 14,11 V 13 H 7 V 15 H 5 V 11 Z";
    public const string About = "M 12,1 A 11,11 0 1 0 12,23 A 11,11 0 1 0 12,1 Z M 12,3 A 9,9 0 1 1 12,21 A 9,9 0 1 1 12,3 Z M 11,6 H 13 V 8 H 11 Z M 11,10 H 13 V 18 H 11 Z";
    internal const string Recovery = "M 3,3 H 5 V 6 A 10,10 0 1 1 3,15 H 5 A 8,8 0 1 0 6,8 H 10 V 10 H 3 Z M 11,6 H 13 V 12 L 16,14 L 15,16 L 11,13 Z";
    internal const string Warning = "M 12,1 L 24,22 H 0 Z M 12,5 L 3.5,20 H 20.5 Z M 11,9 H 13 V 14 H 11 Z M 11,16 H 13 V 18 H 11 Z";

    internal static Grid Beside(Control content, string geometry, bool warning = false)
    {
        var badge = new Border
        {
            Width = 44,
            Height = 44,
            VerticalAlignment = VerticalAlignment.Top,
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.Parse(warning ? "#692D28" : "#23496C")),
            Child = new PathIcon
            {
                Width = 22,
                Height = 22,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.Parse(warning ? "#FFD3CC" : "#D5EAFF")),
                Data = Geometry.Parse(geometry)
            }
        };
        Grid.SetColumn(content, 1);
        return new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 14,
            Children = { badge, content }
        };
    }
}
