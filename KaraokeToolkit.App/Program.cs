using Avalonia;
using System;
using Velopack;

namespace KaraokeToolkit.App;

sealed class Program
{
    // Velopack must handle install/update hooks before Avalonia creates any UI.
    // Other UI or SynchronizationContext-dependent initialization belongs in App.
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build().SetAutoApplyOnStartup(false).Run();
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
