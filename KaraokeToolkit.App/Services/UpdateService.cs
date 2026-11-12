using System.Threading.Tasks;
using Velopack;

namespace KaraokeToolkit.App.Services;

/// <summary>Checks the installed package's channel on Velopack Flow.</summary>
public static class UpdateService
{
    public static async Task<(bool IsInstalled, UpdateInfo? Update)> CheckForUpdatesAsync()
    {
        var manager = new UpdateManager();
        if (!manager.IsInstalled) return (false, null);
        return (true, await manager.CheckForUpdatesAsync().ConfigureAwait(false));
    }
}
