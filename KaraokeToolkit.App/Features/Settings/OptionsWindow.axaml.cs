using KaraokeToolkit.App.Shared;
using KaraokeToolkit.App.Services;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Settings;
using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using KaraokeToolkit.Core;
namespace KaraokeToolkit.App.Features.Settings;

public partial class OptionsWindow : Window
{
    private readonly Action<ConversionOptions, OverwritePolicy, ApplicationTheme, bool, bool, AudioBitratePreferences, ApplicationBackdrop, bool>? save;
    public SettingsWindowSize LastNormalSize { get; private set; } = new();
    public int CurrentTabIndex => OptionsTabs.SelectedIndex;
    public OptionsWindow()
    {
        InitializeComponent();
        Resized += (_, resize) =>
        {
            if (WindowState == WindowState.Normal)
            {
                var size = new SettingsWindowSize(resize.ClientSize.Width, resize.ClientSize.Height);
                if (size.IsValid) LastNormalSize = size;
            }
        };
    }
    public void RestorePresentation(Window owner, SettingsWindowSize size, int tabIndex)
    {
        void FitToScreen()
        {
            var screen = owner.Screens.ScreenFromWindow(owner) ?? owner.Screens.Primary;
            if (screen == null) return;
            var layout = SettingsWindowLayout.Fit(size, screen.WorkingArea.Width / screen.Scaling, screen.WorkingArea.Height / screen.Scaling);
            MinWidth = layout.MinWidth; MinHeight = layout.MinHeight;
            MaxWidth = layout.MaxWidth; MaxHeight = layout.MaxHeight;
            Width = layout.Size.Width; Height = layout.Size.Height;
            LastNormalSize = layout.Size;
        }
        FitToScreen();
        if (tabIndex >= 0 && tabIndex < OptionsTabs.Items.Count && OptionsTabs.Items[tabIndex] is TabItem { IsEnabled: true })
            OptionsTabs.SelectedIndex = tabIndex;
        else OptionsTabs.SelectedIndex = 0;
        Opened += (_, _) =>
        {
            FitToScreen();
            var screen = owner.Screens.ScreenFromWindow(owner) ?? owner.Screens.Primary;
            if (screen == null) return;
            var frame = FrameSize ?? new Avalonia.Size(Width, Height + 40);
            var area = screen.WorkingArea;
            Position = new Avalonia.PixelPoint(
                (int)Math.Clamp(Position.X, area.X, Math.Max(area.X, area.Right - Math.Ceiling(frame.Width * screen.Scaling))),
                (int)Math.Clamp(Position.Y, area.Y, Math.Max(area.Y, area.Bottom - Math.Ceiling(frame.Height * screen.Scaling))));
        };
    }
    public OptionsWindow(ConversionOptions options, OverwritePolicy policy, ApplicationTheme theme, bool validateBeforeConversion,
        bool ignoreLeadingTheForFileOrganization, AudioBitratePreferences audioBitrates,
        Action<ConversionOptions, OverwritePolicy, ApplicationTheme, bool, bool, AudioBitratePreferences, ApplicationBackdrop, bool> save,
        ApplicationBackdrop backdrop, bool rememberInputFolder) : this()
    {
        var model = new OptionsViewModel(options, audioBitrates) { Overwrite = (int)policy, Theme = (int)theme, Backdrop = (int)backdrop,
            ValidateBeforeConversion = validateBeforeConversion, IgnoreLeadingTheForFileOrganization = ignoreLeadingTheForFileOrganization,
            RememberInputFolder = rememberInputFolder };
        model.MarkApplied();
        DataContext = model;
        this.save = save;
    }
    private async void BrowseCdrdao_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not OptionsViewModel model) return;
        try
        {
            var files = await ModalDialog.RunAsync(this, () => StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            { Title = "Choose cdrdao executable", AllowMultiple = false }));
            if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
                model.CdrdaoExecutablePath = path;
        }
        catch (Exception error) { model.Error = "Could not choose cdrdao: " + error.Message; }
    }
    private bool ApplyChanges() => DataContext is OptionsViewModel model &&
        (!model.HasChanges || (save != null && model.Apply((options, policy, theme, validate, ignoreThe, bitrates, backdrop) =>
            save(options, policy, theme, validate, ignoreThe, bitrates, backdrop, model.RememberInputFolder))));
    private async void ResetDefaults_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not OptionsViewModel model) return;
        var dialog = new Window
        {
            Title = WindowTitles.For("Reset Settings"), Width = 470, CanResize = false,
            SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var cancel = new Button { Content = "Cancel" };
        var reset = new Button { Content = "Reset to Defaults" };
        bool confirmed = false;
        cancel.Click += (_, _) => dialog.Close();
        reset.Click += (_, _) => { confirmed = true; dialog.Close(); };
        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(24), Spacing = 18,
            Children =
            {
                new TextBlock
                {
                    Text = "Reset all settings in this window to their defaults? This also clears the custom cdrdao path and resets each encoder's bitrate.\n\nUse Apply or OK to save the reset. Explorer integration and your files will not be changed.",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                },
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Spacing = 10,
                    Children = { cancel, reset }
                }
            }
        };
        dialog.Opened += (_, _) => cancel.Focus();
        dialog.KeyDown += (_, key) =>
        {
            if (key.Key == Avalonia.Input.Key.Escape) { key.Handled = true; dialog.Close(); }
        };
        await ModalDialog.ShowAsync(dialog, this);
        if (confirmed) model.ResetToDefaults();
    }
    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        if (ApplyChanges())
            Close();
    }
    private void Apply_Click(object? sender, RoutedEventArgs e) => ApplyChanges();
    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close();
}


