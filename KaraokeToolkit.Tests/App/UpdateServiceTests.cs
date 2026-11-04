using KaraokeToolkit.App.Services;

namespace KaraokeToolkit.Tests.App;

public class UpdateServiceTests
{
    [Fact]
    public async Task ChecksRemainDisabledWithoutAReleaseFeedOrInstalledPackage()
    {
        Assert.False(UpdateService.ChecksEnabled);
        Assert.Null(UpdateService.FeedUrl);
        // The test process is not a Velopack installation. A disabled check must
        // return without constructing an updater or trying to read a remote feed.
        Assert.Null(await UpdateService.CheckForUpdatesAsync());
    }
}
