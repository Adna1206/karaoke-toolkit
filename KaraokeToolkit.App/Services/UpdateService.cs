using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace KaraokeToolkit.App.Services;

/// <summary>Checks GitHub Releases for updates to the installed package's channel.</summary>
public static class UpdateService
{
    private const string ReleaseRepository = "https://github.com/Adna1206/karaoke-toolkit";

    public static async Task<(bool IsInstalled, UpdateInfo? Update)> CheckForUpdatesAsync()
    {
        var source = new GithubSource(ReleaseRepository, accessToken: null, prerelease: true);
        var manager = new UpdateManager(source);
        if (!manager.IsInstalled) return (false, null);
        return (true, await manager.CheckForUpdatesAsync().ConfigureAwait(false));
    }
}
