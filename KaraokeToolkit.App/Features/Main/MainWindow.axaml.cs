using KaraokeToolkit.App.Shared;
using KaraokeToolkit.App.Services;
using KaraokeToolkit.App.Features.Settings;
using KaraokeToolkit.App.Features.Reports;
using KaraokeToolkit.App.Features.DiscRipping;
using KaraokeToolkit.App.Features.About;
using KaraokeToolkit.App.Features.Recipes;
using KaraokeToolkit.App.Features.Tagging;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.FileOperations;
using KaraokeToolkit.Core.Recovery;
using System;
using System.Linq;
using Avalonia.Input;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Media;
using Avalonia.Threading;
using KaraokeToolkit.Core;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
namespace KaraokeToolkit.App.Features.Main;

public partial class MainWindow : Window
{
    private readonly ProgressLogAutoScroll progressLogAutoScroll;
    public MainWindow()
    {
        InitializeComponent();
        if (OperatingSystem.IsWindows()) ConfigureWindowsTitleBar();
        CategoryList.AddHandler(KeyDownEvent, CategoryList_KeyDown, RoutingStrategies.Tunnel);
        OperationList.AddHandler(KeyDownEvent, OperationList_KeyDown, RoutingStrategies.Tunnel);
        progressLogAutoScroll = new(ActivityLog);
        Closed += (_, _) => progressLogAutoScroll.Dispose();
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(DragDrop.DropEvent, OnDrop, RoutingStrategies.Bubble, handledEventsToo: true);
        Opened += async (_, _) =>
        {
            if (DataContext is MainViewModel model)
            {
                model.RipDiscAsync = async () =>
                {
                    var dialog = new DiscRipWindow();
                    var rip = new DiscRipViewModel(model.Options, model.OverwritePolicy, resolve: (conflict, token) => ResolveConflictForOwnerAsync(dialog, conflict, token));
                    dialog.DataContext = rip;
                    await ModalDialog.ShowAsync(dialog, this);
                    return rip.Result;
                };
                model.RestoreQuarantinedFilesAsync = RestoreQuarantinedFilesAsync;
                model.ConfigureTaggingAsync = async viewModel => await ModalDialog.ShowAsync<TaggingRequest?>(new TaggingWindow { DataContext = viewModel }, this);
                model.ResolveConflictAsync = ResolveConflictAsync;
                model.ConfirmRecoveryAsync = ConfirmRecoveryAsync;
                await model.CheckForRecoveryAsync();
                // Recovery briefly disables editing; focus once the home list is ready.
                if (!model.ShowProgress)
                {
                    model.SelectedSection = model.VisibleSections.FirstOrDefault();
                    CategoryList.SelectedIndex = 0;
                    FocusSelection(CategoryList);
                }
            }
        };
        Closing += (_, e) => { if (DataContext is MainViewModel { IsBusy: true }) e.Cancel = true; };
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

    private void Category_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is MainViewModel { IsBusy: false } model &&
            e.AddedItems.Count > 0 && e.AddedItems[0] is OperationSectionViewModel section)
            model.SelectedSection = section;
    }

    private void CategoryList_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Right && e.KeyModifiers == KeyModifiers.None)
            e.Handled = FocusSelection(OperationList);
    }

    private void OperationList_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Left && e.KeyModifiers == KeyModifiers.None)
            e.Handled = FocusSelection(CategoryList);
    }

    private static bool FocusSelection(ListBox list)
    {
        list.UpdateLayout();
        if (!list.IsEnabled || list.ItemCount == 0) return false;
        if (list.SelectedIndex < 0) list.SelectedIndex = 0;
        if (list.SelectedItem is not { } selectedItem) return false;
        // A newly selected category may not have realised its operation rows yet.
        // Select before focusing, then allow layout to materialise the chosen row.
        list.UpdateLayout();
        list.ScrollIntoView(selectedItem);
        list.UpdateLayout();
        if (list.ContainerFromIndex(list.SelectedIndex)?.Focus(NavigationMethod.Directional) != true)
        {
            // Focus can return false even when a container exists. Keep keyboard
            // focus in the destination while waiting for its layout to complete.
            list.Focus(NavigationMethod.Directional);
            Dispatcher.UIThread.Post(() =>
            {
                if (list.IsEnabled && list.IsKeyboardFocusWithin)
                    list.ContainerFromIndex(list.SelectedIndex)?.Focus(NavigationMethod.Directional);
            }, DispatcherPriority.Loaded);
        }
        return true;
    }

    private void Operation_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        // Clearing the other group's selection must not clear the shared operation.
        if (DataContext is MainViewModel { IsBusy: false } model &&
            e.AddedItems.Count > 0 && e.AddedItems[0] is ActionChoice choice)
            model.SelectedOperation = choice;
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
            {
                model.AddPickedInputs(folders.Select(f => f.TryGetLocalPath()).OfType<string>().ToArray());
                foreach (var path in model.CurrentInputs)
                if (await model.CheckForRecoveryAsync(path))
                    break;
            }
        }
        catch (Exception error) { model.Status = "Could not select folders: " + error.Message; }
    }
    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = DataContext is MainViewModel { CanChooseSource: true } && e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }
    private async void OnDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        if (DataContext is not MainViewModel { CanChooseSource: true } model)
            return;
        try
        {
            var files = e.DataTransfer.TryGetFiles();
            if (files != null)
                model.AddInputs(files.Select(f => f.TryGetLocalPath()).OfType<string>());
            foreach (string input in model.CurrentInputs)
            {
                string folder = System.IO.Directory.Exists(input) ? input : System.IO.Path.GetDirectoryName(input)!;
                if (await model.CheckForRecoveryAsync(folder))
                    break;
            }
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
            if (!await ConfirmRestoreAsync("Restore Quarantined Files", "Restore the files listed in this Damaged manifest to their original paths? Existing files will never be replaced.\n\n" + path, "Restore"))
                return;
            model.IsBusy = true;
            await new QuarantineService().RestoreAsync(path);
            if (model.LastReport is not null)
                new ResultsViewModel(model).Replace(model.LastReport.Entries.Where(r => r.Outputs.Contains(path)).Select(r => r with { Status = "Restored", Message = "Files restored to their original paths. Scan again before further action.", Outputs = [] }).ToArray());
            model.Status = "Files restored. Run validation again to refresh their status.";
            model.StatusOutcome = RunStatus.Success;
        }
        catch (Exception error) { model.StatusOutcome = RunStatus.Failure; model.Status = "Restore stopped: " + error.Message; }
        finally { model.IsBusy = false; }
    }
    private async Task<bool> ConfirmRestoreAsync(string title, string text, string action)
    {
        bool accepted = false;
        var dialog = new Window { Title = WindowTitles.For(title), Width = 610, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var cancel = new Button { Content = "Cancel" };
        var proceed = new Button { Content = action };
        cancel.Click += (_, _) => dialog.Close();
        proceed.Click += (_, _) => { accepted = true; dialog.Close(); };
        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(24),
            Spacing = 18,
            Children =
        {
            new ScrollViewer { MaxHeight = 350, Content = new TextBlock { Text = text, TextWrapping = Avalonia.Media.TextWrapping.Wrap } },
            new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 12, Children = { cancel, proceed } }
        }
        };
        dialog.Opened += (_, _) => cancel.Focus();
        await ModalDialog.ShowAsync(dialog, this);
        return accepted;
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
        if (DataContext is MainViewModel { IsBusy: false } model)
        {
            var dialog = new OptionsWindow(model.Options, model.OverwritePolicy, model.Theme, model.ValidateBeforeConversion,
                model.IgnoreLeadingTheForFileOrganization, model.AudioBitrates, model.SaveOptions, model.Backdrop, model.RememberInputFolder);
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
    private void Back_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel { IsBusy: false } model)
            model.ShowProgress = false;
    }
    private void Exit_Click(object? sender, RoutedEventArgs e) => Close();
    private async void About_Click(object? sender, RoutedEventArgs e) => await ModalDialog.ShowAsync(new AboutWindow(), this);
    private async void CheckForUpdates_Click(object? sender, RoutedEventArgs e)
    {
        var menuItem = CheckForUpdatesMenuItem;
        if (!menuItem.IsEnabled) return;
        var header = menuItem.Header;
        menuItem.IsEnabled = false;
        menuItem.Header = "Checking for updates…";
        string message;
        try
        {
            var (isInstalled, update) = await UpdateService.CheckForUpdatesAsync();
            message = (isInstalled, update) switch
            {
                (false, _) => "Install Karaoke Toolkit with Velopack to check for updates.",
                (true, null) => "No newer version is available.",
                (true, { } available) => $"Version {available.TargetFullRelease.Version} is available."
            };
        }
        catch (Exception error)
        {
            System.Diagnostics.Trace.TraceWarning("Velopack Flow update check failed: {0}", error);
            message = "Could not check for updates. Try again later.";
        }
        finally
        {
            menuItem.Header = header;
            menuItem.IsEnabled = true;
        }

        if (IsVisible)
            await ShowInfo("Check for updates", message);
    }

    private async void Help_Click(object? sender, RoutedEventArgs e) => await ShowInfo("Help", "Choose a category and operation, add source files or folders, then click Start. Add buttons and drag-and-drop accumulate selections; Clear removes them. Star operations to find them in Favourites. Disc ripping and restoration ask for their inputs after Start.\n\nUse Tools → Settings to configure recursion, deletion, compression, encoding and existing-file handling. Output files are written beside source files unless the operation asks for a destination.\n\nCancel discards unpublished work; completed files remain. Review the event log and View Last Run Report when processing finishes.");
    private async System.Threading.Tasks.Task ShowInfo(string title, string text)
    {
        var dialog = new Window { Title = WindowTitles.For(title), Width = 480, SizeToContent = SizeToContent.Height, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var close = new Button { Content = "Close", HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => dialog.Close();
        dialog.Content = new StackPanel { Margin = new Avalonia.Thickness(24), Spacing = 20, Children = { new TextBlock { Text = text, TextWrapping = Avalonia.Media.TextWrapping.Wrap }, close } };
        await ModalDialog.ShowAsync(dialog, this);
    }
    private async Task<bool> ConfirmRecoveryAsync(RecoveryJob job)
    {
        var dialog = new Window { Title = WindowTitles.For("Recovery"), Width = 580, SizeToContent = SizeToContent.Height, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        bool resume = false;
        var recover = new Button { Content = "Recover and resume", MinWidth = 160, IsEnabled = job.Problem == null };
        var later = new Button { Content = "Later", MinWidth = 90 };
        recover.Click += (_, _) => { resume = true; dialog.Close(); };
        later.Click += (_, _) => dialog.Close();
        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(24),
            Spacing = 18,
            Children =
            {
                new TextBlock { Text = ApplicationInfo.Name + " did not finish this run.", FontWeight = Avalonia.Media.FontWeight.SemiBold },
                new TextBlock { Text = $"{job.Folder}\n\n{job.Action}\n{job.Completed} of {job.Total} {(job.Kind == RecoveryKind.Recipe ? "recipe steps" : job.Kind == RecoveryKind.Tagging ? "files" : "source groups")} recorded.", TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                new TextBlock { Text = job.Problem ?? (job.Kind == RecoveryKind.Recipe
                    ? "Recovery uses the saved recipe snapshot, checks the interrupted step, and continues the remaining steps. Completed steps are kept, even if the recipe library has since changed."
                    : job.Kind == RecoveryKind.Tagging ? "Recovery checks the interrupted tag publication, then tags remaining files using the saved preview and options. Completed files are kept."
                    : "Recovery checks any interrupted file publication, then resumes unfinished work with the saved action and settings. Completed groups are kept.") + " If files have changed, recovery stops and retains the recovery files. Later leaves everything as it is.", TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 10, Children = { later, recover } }
            }
        };
        dialog.Opened += (_, _) => later.Focus();
        await ModalDialog.ShowAsync(dialog, this);
        return resume;
    }
    private Task<ConflictDecision> ResolveConflictAsync(OutputConflict conflict, CancellationToken token)
        => ResolveConflictForOwnerAsync(this, conflict, token);
    private static async Task<ConflictDecision> ResolveConflictForOwnerAsync(Window owner, OutputConflict conflict, CancellationToken token)
    {
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            token.ThrowIfCancellationRequested();
            var dialog = new Window
            {
                Title = WindowTitles.For("Output Conflict"),
                Width = 570,
                SizeToContent = SizeToContent.Height,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            var decision = ConflictDecision.Cancel;
            var skip = new Button { Content = "Skip", MinWidth = 90 };
            var replace = new Button { Content = "Replace", MinWidth = 90 };
            var cancel = new Button { Content = "Cancel conversion", MinWidth = 140 };
            skip.Click += (_, _) => { decision = ConflictDecision.Skip; dialog.Close(); };
            replace.Click += (_, _) => { decision = ConflictDecision.Replace; dialog.Close(); };
            cancel.Click += (_, _) => dialog.Close();
            dialog.Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(24),
                Spacing = 18,
                Children ={
                new TextBlock {Text="These output files already exist:",FontWeight=Avalonia.Media.FontWeight.SemiBold},
                new ScrollViewer {MaxHeight=220,Content=new TextBlock {Text=string.Join("\n",conflict.ExistingFiles),TextWrapping=Avalonia.Media.TextWrapping.Wrap}},
                new TextBlock {Text="Skip keeps the existing files and the source. Replace updates this output group. Change the default under Options → General.",TextWrapping=Avalonia.Media.TextWrapping.Wrap},
                new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Spacing=10,Children={skip,replace,cancel}}
            }
            };
            dialog.Opened += (_, _) => skip.Focus();
            using var registration = token.Register(() => Dispatcher.UIThread.Post(() => dialog.Close()));
            await ModalDialog.ShowAsync(dialog, owner);
            return decision;
        });
    }
}


