using KaraokeToolkit.App.Services;
using KaraokeToolkit.App.Features.Settings;
using KaraokeToolkit.App.Features.Main;
using KaraokeToolkit.Core.Settings;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using KaraokeToolkit.Core;
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
            desktop.MainWindow = new MainWindow
            {
                DataContext = model,
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}

