using KaraokeToolkit.App.Features.Settings;
using KaraokeToolkit.App.Features.Main;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
namespace KaraokeToolkit.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        WindowAppearance.Initialize();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow { IsEnabled = false };
            window.Opened += (_, _) =>
            {
                // Give the main window a render frame before loading the desktop state.
                window.RequestAnimationFrame(_ => window.RequestAnimationFrame(async _ =>
                {
                    if (window.IsVisible)
                        await InitializeMainWindowAsync(desktop, window);
                }));
            };
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async System.Threading.Tasks.Task InitializeMainWindowAsync(
        IClassicDesktopStyleApplicationLifetime desktop, MainWindow window)
    {
        var model = new MainViewModel();
        WindowAppearance.SetTheme(model.Theme);
        model.ThemeChanged += WindowAppearance.SetTheme;
        WindowAppearance.SetBackdrop(model.Backdrop);
        model.BackdropChanged += WindowAppearance.SetBackdrop;
        if (desktop.Args is { Length: > 0 })
            try
            {
                model.SetInputs(Services.ExplorerIntegration.ParseInputs(desktop.Args));
            }
            catch (System.Exception error) { model.Status = "Could not open selection: " + error.Message; }

        window.DataContext = model;
        window.IsEnabled = true;
        await window.CompleteStartupAsync(model);
    }
}

