using KaraokeToolkit.App.Shared;
using KaraokeToolkit.App.Features.Main;
using KaraokeToolkit.App.Features.Reports.Dialogs;
using KaraokeToolkit.Core.FileOperations;
using KaraokeToolkit.Core.Reporting;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using KaraokeToolkit.App.Services;
using KaraokeToolkit.Core;

namespace KaraokeToolkit.App.Features.Reports;

public partial class ResultsWindow : Window
{
    public bool RetryRequested
    {
        get; private set;
    }
    public ResultsWindow()
    {
        InitializeComponent();
        _ = new BusyNavigation(this);
    }
    public ResultsWindow(MainViewModel model) : this() => DataContext = new ResultsViewModel(model);
    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
    private void Retry_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ResultsViewModel { CanRetry: true })
        {
            RetryRequested = true;
            Close();
        }
    }
    private void Open_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ResultsViewModel model)
            return;
        try
        {
            if (ResultsList.SelectedItem is not ReportRow row)
            {
                model.MessageSeverity = StatusKind.Warning;
                model.Message = "Select a result first.";
                return;
            }
            FolderLauncher.Open(row.Entry.Outputs.FirstOrDefault(p => File.Exists(p) || Directory.Exists(p)) ?? row.Source);
        }
        catch (Exception error) { model.MessageSeverity = StatusKind.Failure; model.Message = error.Message; }
    }
    private async void Save_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ResultsViewModel model)
            return;
        try
        {
            var file = await ModalDialog.RunAsync(this, () => StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save Last Run Report",
                SuggestedFileName = ApplicationInfo.Name + "-report.txt",
                DefaultExtension = "txt",
                FileTypeChoices = new[] { new FilePickerFileType("Text report") { Patterns = ["*.txt"] }, new FilePickerFileType("JSON report") { Patterns = ["*.json"] } }
            }));
            if (file == null)
                return;
            await using var output = await file.OpenWriteAsync();
            output.SetLength(0);
            await using var writer = new StreamWriter(output, new UTF8Encoding(false));
            await writer.WriteAsync(Path.GetExtension(file.Name).Equals(".json", StringComparison.OrdinalIgnoreCase) ? model.Report.ToJson() : model.Report.ToText());
            model.MessageSeverity = StatusKind.Success;
            model.Message = "Report saved.";
        }
        catch (Exception error) { model.MessageSeverity = StatusKind.Failure; model.Message = "Could not save report: " + error.Message; }
    }
    private async void Move_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ResultsViewModel model || model.IsBusy)
            return;
        var selection = ResultsList.SelectedItems?.Cast<ReportRow>().Where(r => r.CanMove).Select(r => r.Finding!).ToArray() ?? [];
        if (selection.Length == 0)
        {
            model.MessageSeverity = StatusKind.Warning;
            model.Message = "Select validation issues to move. Valid and unreadable items are not moved.";
            return;
        }
        string files = string.Join("\n", selection.SelectMany(f => f.Files).Select(f => f.Path));
        if (!await MoveFilesToDamagedDialog.ShowAsync(this, files))
            return;
        model.IsBusy = true;
        model.MessageSeverity = StatusKind.Info;
        model.Message = "Checking fingerprints and moving selected groups...";
        try
        {
            var results = await new QuarantineService().MoveAsync(selection);
            model.Replace(results.Select(r => new ReportEntry(r.Source, r.Succeeded ? "Moved" : "Move failed", r.Message,
                r.Manifest == null ? r.MovedFiles : r.MovedFiles.Concat([r.Manifest]).ToArray())));
            int moved = results.Count(result => result.Succeeded);
            int failed = results.Count - moved;
            model.MessageSeverity = failed > 0 ? StatusKind.Failure : StatusKind.Success;
            model.Message = $"{moved} group(s) moved. {failed} could not be moved. Restore is available using each manifest.";
        }
        catch (Exception error) { model.MessageSeverity = StatusKind.Failure; model.Message = error.Message; }
        finally { model.IsBusy = false; }
    }
}
