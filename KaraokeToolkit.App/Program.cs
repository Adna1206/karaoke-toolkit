using Avalonia;
using System;
using KaraokeToolkit.App.Services;
using Velopack;

namespace KaraokeToolkit.App;

sealed class Program
{
    // Velopack must handle install/update hooks before Avalonia creates any UI.
    // Other UI or SynchronizationContext-dependent initialization belongs in App.
    [STAThread]
    public static void Main(string[] args)
    {
        var lifecycle = VelopackApp.Build().SetAutoApplyOnStartup(false);
        if (OperatingSystem.IsWindows())
            lifecycle.OnAfterInstallFastCallback(_ => ExplorerIntegration.ConfigureAfterSetup());
        lifecycle.Run();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
