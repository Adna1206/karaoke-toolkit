using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace KaraokeToolkit.App.Services;

/// <summary>Checks GitHub Releases and returns the manager for the installed package's channel.</summary>
public static class UpdateService
{
    private const string ReleaseRepository = "https://github.com/Adna1206/karaoke-toolkit";

    public static async Task<(UpdateManager Manager, bool IsInstalled, UpdateInfo? Update)> CheckForUpdatesAsync()
    {
        var source = new GithubSource(ReleaseRepository, accessToken: null, prerelease: true);
        var manager = new UpdateManager(source);
        if (!manager.IsInstalled) return (manager, false, null);
        return (manager, true, await manager.CheckForUpdatesAsync().ConfigureAwait(false));
    }
}
