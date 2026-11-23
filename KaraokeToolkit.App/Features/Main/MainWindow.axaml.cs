using KaraokeToolkit.App.Shared;
using KaraokeToolkit.App.Features.AlphabeticalGrouping;
using KaraokeToolkit.App.Features.Settings;
using KaraokeToolkit.App.Features.Main.Dialogs;
using KaraokeToolkit.App.Features.Reports;
using KaraokeToolkit.App.Features.DiscRipping;
using KaraokeToolkit.App.Features.About;
using KaraokeToolkit.App.Features.Recipes;
using KaraokeToolkit.App.Features.Tagging;
using KaraokeToolkit.App.Features.FilenameRename;
using KaraokeToolkit.App.Features.TrackGrouping;
using KaraokeToolkit.Core.FileOperations;
using System;
using System.Linq;
using Avalonia.Input;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Media;
using Avalonia.Threading;
using KaraokeToolkit.Core;
using System.Threading.Tasks;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
namespace KaraokeToolkit.App.Features.Main;

public partial class MainWindow : Window
{
    private readonly BusyNavigation navigation;
    private Action? cancelPreview;

    public MainWindow()
    {
        InitializeComponent();
        if (OperatingSystem.IsWindows()) ConfigureWindowsTitleBar();
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(DragDrop.DropEvent, OnDrop, RoutingStrategies.Bubble, handledEventsToo: true);
        navigation = new(this, "Exit", GetNavigationCancellation, GetNavigationOperation);
    }

    internal async Task CompleteStartupAsync(MainViewModel model)
    {
        model.RipDiscAsync = async () =>
        {
            var dialog = new DiscRipWindow();
            var rip = new DiscRipViewModel(model.Options, model.OverwritePolicy, resolve: (conflict, token) => OutputConflictDialog.ShowAsync(dialog, conflict, token));
            dialog.DataContext = rip;
            await ModalDialog.ShowAsync(dialog, this);
            return rip.Result;
        };
        model.RestoreQuarantinedFilesAsync = RestoreQuarantinedFilesAsync;
        model.ConfigureTaggingAsync = viewModel =>
        {
            var preview = new TaggingPreviewView(viewModel, ConfirmPreviewBackAsync);
            return ShowPreviewAsync(preview, preview.ReviewAsync, preview.CancelPreview);
        };
        model.ConfigureTrackGroupingAsync = viewModel =>
        {
            var preview = new TrackGroupingPreviewView(viewModel, ConfirmPreviewBackAsync);
            return ShowPreviewAsync(preview, preview.ReviewAsync, preview.CancelPreview);
        };
        model.ConfigureAlphabeticalGroupingAsync = viewModel =>
        {
            var preview = new AlphabeticalGroupingPreviewView(viewModel, ConfirmPreviewBackAsync);
            return ShowPreviewAsync(preview, preview.ReviewAsync, preview.CancelPreview);
        };
        model.ConfigureFilenameRenameAsync = viewModel =>
        {
            var preview = new FilenameRenamePreviewView(viewModel, ConfirmPreviewBackAsync);
            return ShowPreviewAsync(preview, preview.ReviewAsync, preview.CancelPreview);
        };
        model.ResolveConflictAsync = (conflict, token) => OutputConflictDialog.ShowAsync(this, conflict, token);
        model.ResolveConflictsAsync = (conflicts, token) => OutputConflictReviewWindow.ShowAsync(this, conflicts, token);
        model.ConfirmRecoveryAsync = job => InterruptedRunDialog.ShowAsync(this, job);
        await model.CheckForRecoveryAsync();
        // Recovery briefly disables editing; focus once the home list is ready.
        if (!model.ShowProgress)
        {
            model.SelectedSection = model.VisibleSections.FirstOrDefault();
            OperationPicker.FocusFirstCategory();
        }
        if (model.CheckForUpdatesOnStartup)
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            if (IsVisible) await CheckForUpdatesAsync(manual: false);
        }
    }

    private async Task<T?> ShowPreviewAsync<T>(Control preview, Func<Task<T?>> review, Action cancel) where T : class
    {
        cancelPreview = cancel;
        OperationPreview.Content = preview;
        OperationWorkspace.IsVisible = false;
        OperationPreview.IsVisible = true;
        try { return await review(); }
        finally
        {
            cancelPreview = null;
            OperationPreview.IsVisible = false;
            OperationPreview.Content = null;
            OperationWorkspace.IsVisible = true;
        }
    }

    private Task<bool> ConfirmPreviewBackAsync(bool isPreparing, bool hasEdits) =>
        isPreparing ? BusyOperationDialog.ShowAsync(this, "Go back", canCancel: true, isPreview: true)
            : hasEdits ? DiscardPreviewEditsDialog.ShowAsync(this) : Task.FromResult(true);

    private Action? GetNavigationCancellation() =>
        cancelPreview ?? (DataContext is MainViewModel { CanCancelActiveRun: true } model
            ? () => model.CancelCommand.Execute(null) : null);

    private Task? GetNavigationOperation()
    {
        if (DataContext is not MainViewModel model)
            return null;
        return new[]
        {
            model.StartCommand.ExecutionTask, model.CheckRecoveryCommand.ExecutionTask,
            model.ValidateCommand.ExecutionTask, model.RunDiscRipCommand.ExecutionTask
        }.FirstOrDefault(task => task is { IsCompleted: false });
    }

    private void ConfigureWindowsTitleBar()
    {
        const double titleBarHeight = 36;
        WindowDecorationsTheme = (Avalonia.Styling.ControlTheme)Resources["MainWindowDecorations"]!;
        ExtendClientAreaTitleBarHeightHint = titleBarHeight;
        ExtendClientAreaToDecorationsHint = true;
        MenuRow.Height = titleBarHeight;
        Grid.SetColumnSpan(MainMenu, 1);
        Grid.SetColumn(MainMenu, 1);
        TitleBarIdentity.IsVisible = true;
        MainHeader.IsVisible = false;
        MainMenu.Classes.Add("titleBar");
        MainMenu.VerticalAlignment = VerticalAlignment.Center;
        MainMenu.Background = Brushes.Transparent;
        WindowDecorationProperties.SetElementRole(MainMenu, WindowDecorationsElementRole.User);
        WindowDecorationProperties.SetElementRole(TitleBarIdentity, WindowDecorationsElementRole.TitleBar);
        WindowDecorationProperties.SetElementRole(TitleBarDragArea, WindowDecorationsElementRole.TitleBar);
        TitleBarDragArea.IsVisible = true;
    }

    private async void BrowseFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { CanChooseSource: true } model)
            return;
        try
        {
            using var startFolder = await GetInputPickerStartFolderAsync(model);
            var folders = await ModalDialog.RunAsync(this, () => StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Choose source folders",
                AllowMultiple = model.SelectedOperation?.Definition.AllowsMultipleSources == true,
                SuggestedStartLocation = startFolder
            }));
            if (folders.Count > 0 && !model.IsBusy)
                model.AddPickedInputs(folders.Select(f => f.TryGetLocalPath()).OfType<string>().ToArray());
        }
        catch (Exception error) { model.Status = "Could not select folders: " + error.Message; }
    }
    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = DataContext is MainViewModel { CanChooseSource: true } && e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }
    private void OnDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        if (DataContext is not MainViewModel { CanChooseSource: true } model)
            return;
        try
        {
            var files = e.DataTransfer.TryGetFiles();
            if (files != null)
                model.AddInputs(files.Select(f => f.TryGetLocalPath()).OfType<string>());
        }
        catch (Exception error) { model.Status = "Could not use dropped files: " + error.Message; }
    }
    private async void BrowseFiles_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { CanChooseFiles: true } model)
            return;
        try
        {
            var extensions = model.SelectedOperation?.SourceFileExtensions;
            using var startFolder = await GetInputPickerStartFolderAsync(model);
            var files = await ModalDialog.RunAsync(this, () => StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Choose source files",
                AllowMultiple = model.SelectedOperation?.Definition.AllowsMultipleSources == true,
                SuggestedStartLocation = startFolder,
                FileTypeFilter = [new FilePickerFileType(extensions == null ? "All files" : "Supported files")
                    { Patterns = extensions?.Select(extension => "*" + extension).ToArray() ?? ["*"] }]
            }));
            if (files.Count > 0 && !model.IsBusy)
                model.AddPickedInputs(files.Select(f => f.TryGetLocalPath()).OfType<string>().ToArray());
        }
        catch (Exception error) { model.Status = "Could not select files: " + error.Message; }
    }
    private async Task<IStorageFolder?> GetInputPickerStartFolderAsync(MainViewModel model)
    {
        if (model.InputPickerStartFolder is not { } folder) return null;
        try { return await StorageProvider.TryGetFolderFromPathAsync(folder); }
        catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException or NotSupportedException)
        { return null; }
    }

    private async Task RestoreQuarantinedFilesAsync()
    {
        if (DataContext is not MainViewModel model || model.IsBusy)
            return;
        try
        {
            var files = await ModalDialog.RunAsync(this, () => StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Damaged batch manifest.json",
                AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType("Damaged manifest") { Patterns = ["manifest.json"] } }
            }));
            if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path)
                return;
            if (!await RestoreQuarantinedFilesDialog.ShowAsync(this, path))
                return;
            model.IsBusy = true;
            model.Status = "Checking fingerprints and restoring files…";
            model.IsRunProgressIndeterminate = true;
            await new QuarantineService().RestoreAsync(path);
            if (model.LastReport is not null)
                new ResultsViewModel(model).Replace(model.LastReport.Entries.Where(r => r.Outputs.Contains(path)).Select(r => r with { Status = "Restored", Message = "Files restored to their original paths. Scan again before further action.", Outputs = [] }).ToArray());
            model.Status = "Files restored. Run validation again to refresh their status.";
            model.StatusOutcome = RunStatus.Success;
        }
        catch (Exception error) { model.StatusOutcome = RunStatus.Failure; model.Status = "Restore stopped: " + error.Message; }
        finally
        {
            model.IsRunProgressIndeterminate = false;
            model.IsBusy = false;
        }
    }
    private async void Results_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { IsBusy: false } model)
            return;
        if (model.LastReport is null)
            return;
        var results = new ResultsWindow(model);
        await ModalDialog.ShowAsync(results, this);
        if (results.RetryRequested)
            await model.RetryFailedAsync();
    }
    private async void Options_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel { IsBusy: false } model && model.RefreshSharedSettings())
        {
            var dialog = new SettingsWindow(model.Options, model.OverwritePolicy, model.Theme, model.ValidateBeforeConversion,
                model.IgnoreLeadingTheForFileOrganization, model.AudioBitrates, model.SaveOptions, model.Backdrop, model.RememberInputFolder, model.RecoveryVerification, model.SaveResumableRun, model.CheckForUpdatesOnStartup);
            dialog.RestorePresentation(this, model.OptionsWindowSize, model.OptionsTabIndex);
            await ModalDialog.ShowAsync(dialog, this);
            model.RememberOptionsWindow(dialog.LastNormalSize, dialog.CurrentTabIndex);
        }
    }
    private async void Recipes_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { IsBusy: false } model) return;
        try
        {
            var dialog = new RecipeBuilderWindow { DataContext = new RecipeBuilderViewModel(model) };
            await ModalDialog.ShowAsync(dialog, this);
            model.ReloadRecipes();
        }
        catch (Exception error) { model.StatusOutcome = RunStatus.Failure; model.Status = "Could not start recipe: " + error.Message; }
    }
    private async void Back_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel model)
            await navigation.GoBackAsync(() => model.ShowProgress = false);
    }
    private void Exit_Click(object? sender, RoutedEventArgs e) => Close();
    private async void About_Click(object? sender, RoutedEventArgs e) => await ModalDialog.ShowAsync(new AboutWindow(), this);
    private async void CheckForUpdates_Click(object? sender, RoutedEventArgs e) => await CheckForUpdatesAsync(manual: true);

    private async Task CheckForUpdatesAsync(bool manual)
    {
        var menuItem = CheckForUpdatesMenuItem;
        if (!menuItem.IsEnabled) return;
        var header = menuItem.Header;
        menuItem.IsEnabled = false;
        menuItem.Header = "Checking for updates…";
        try
        {
            await SoftwareUpdatesDialog.CheckAsync(this, manual, () => DataContext is MainViewModel { IsBusy: true });
        }
        finally
        {
            menuItem.Header = header;
            menuItem.IsEnabled = true;
        }
    }

    private async void Help_Click(object? sender, RoutedEventArgs e) => await OperationHelpDialog.ShowAsync(this);
}
