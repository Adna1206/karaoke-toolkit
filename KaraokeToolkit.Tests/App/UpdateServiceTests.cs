using KaraokeToolkit.App.Services;
using Velopack;

namespace KaraokeToolkit.Tests.App;

public class UpdateServiceTests
{
    [Fact]
    public async Task DevelopmentBuildReportsThatItIsNotInstalled()
    {
        VelopackApp.Build().SetAutoApplyOnStartup(false).Run();
        var (manager, isInstalled, update) = await UpdateService.CheckForUpdatesAsync();
        Assert.False(manager.IsInstalled);
        Assert.False(isInstalled);
        Assert.Null(update);
    }
}
