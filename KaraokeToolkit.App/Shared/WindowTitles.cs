using KaraokeToolkit.Core;

namespace KaraokeToolkit.App.Shared;

/// <summary>Consistent titles for application-owned windows; native file pickers use task descriptions.</summary>
public static class WindowTitles
{
    public static string Settings => For("Settings");
    public static string About => For("About");
    public static string LastRunReport => For("Last Run Report");
    public static string DiscRipping => For("Rip CD+G (Experimental)");
    public static string Tagging => For("Tag Karaoke Files");

    public static string For(string windowName) => ApplicationInfo.Name + " — " + windowName;
}
