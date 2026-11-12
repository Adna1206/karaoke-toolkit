using System.Reflection;

namespace KaraokeToolkit.Core;

/// <summary>Shared display name and release version; technical identifiers remain independent.</summary>
public static class ApplicationInfo
{
    public const string Name = "Karaoke Toolkit";
    public static string Version { get; } = typeof(ApplicationInfo).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(ApplicationInfo).Assembly.GetName().Version?.ToString(3) ?? "Unknown";
}
