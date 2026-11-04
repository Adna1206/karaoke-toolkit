using System.Threading.Tasks;
using Velopack;

namespace KaraokeToolkit.App.Services;

/// <summary>App-only update integration. Remains dormant until a release feed and UI are ready.</summary>
public static class UpdateService
{
    // Developer switches, not user preferences. Configure the source and enable together
    // when release hosting is ready. No UpdateManager is constructed while disabled.
    public static bool ChecksEnabled { get; } = false;
    public static string? FeedUrl { get; } = null;

    /// <summary>Checks only; never downloads, installs, or restarts the application.
    /// The future UI caller must handle network errors and ask before applying updates.</summary>
    public static async Task<UpdateInfo?> CheckForUpdatesAsync()
    {
        if (!ChecksEnabled || string.IsNullOrWhiteSpace(FeedUrl)) return null;

        var manager = new UpdateManager(FeedUrl);
        if (!manager.IsInstalled) return null; // Ordinary development builds have no update installation.
        return await manager.CheckForUpdatesAsync().ConfigureAwait(false);
    }
}
