using KaraokeToolkit.App.Shared;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.DiscRipping;
namespace KaraokeToolkit.App.Features.DiscRipping;

public partial class RipTrackViewModel(DiscTrack track) : ObservableObject
{
    public DiscTrack Track { get; } = track;
    public string Label => Track.ToString();
    [ObservableProperty] public partial bool Selected { get; set; } = true;
}
public partial class DiscRipViewModel : ViewModelBase
{
    private readonly IDiscReader reader;
    private readonly ConversionOptions options;
    private readonly OverwritePolicy policy;
    private readonly Func<OutputConflict, CancellationToken, Task<ConflictDecision>>? resolve;
    private CancellationTokenSource? cancellation;
    public ConversionResult? Result { get; private set; }
    public ObservableCollection<OpticalDisc> Drives { get; } = new();
    public ObservableCollection<RipTrackViewModel> Tracks { get; } = new();
    public IReadOnlyList<string> Formats { get; } = new[] { "BIN", "Audio+G", "ZIP" };
    [ObservableProperty] public partial OpticalDisc? SelectedDrive { get; set; }
    [ObservableProperty] public partial string Destination { get; set; } = "";
    [ObservableProperty] public partial string DiscName { get; set; } = "DISC";
    [ObservableProperty] public partial int Format { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanEdit)), NotifyCanExecuteChangedFor(nameof(RefreshCommand)), NotifyCanExecuteChangedFor(nameof(StartCommand)), NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial string Status { get; set; } = "Refresh to detect drives and audio tracks.";
    [ObservableProperty] public partial int Progress { get; set; }
    public bool CanEdit => !IsBusy;
    public bool CanStart => !IsBusy && Result == null && options.ExperimentalDiscRipping && SelectedDrive is { SupportsCdg: true } && Tracks.Any(t => t.Selected);
    public DiscRipViewModel(ConversionOptions options, OverwritePolicy policy, IDiscReader? reader = null,
        Func<OutputConflict, CancellationToken, Task<ConflictDecision>>? resolve = null)
    { this.options = options; this.policy = policy; this.reader = reader ?? new NativeDiscReader(options.CdrdaoExecutablePath); this.resolve = resolve; }
    partial void OnSelectedDriveChanged(OpticalDisc? value)
    {
        Tracks.Clear();
        foreach (var track in value?.Tracks ?? [])
        {
            var model = new RipTrackViewModel(track);
            model.PropertyChanged += (_, _) => StartCommand.NotifyCanExecuteChanged();
            Tracks.Add(model);
        }
        StartCommand.NotifyCanExecuteChanged();
    }
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        using var source = new CancellationTokenSource(); cancellation = source;
        try
        {
            Drives.Clear(); SelectedDrive = null; Result = null;
            foreach (var drive in await reader.ScanAsync(source.Token)) Drives.Add(drive);
            SelectedDrive = Drives.FirstOrDefault(d => d.SupportsCdg && d.Tracks.Length > 0) ?? Drives.FirstOrDefault();
            Status = Drives.Count == 0 ? "No optical drives detected." : SelectedDrive?.SupportsCdg != true ? "No drive reports CD+G reading support." : Tracks.Count == 0 ? "Insert an audio/CD+G disc, then Refresh." : "Select tracks and an output folder. Reading is experimental.";
        }
        catch (OperationCanceledException) { Status = "Drive scan cancelled or timed out."; }
        catch (Exception error) { Status = "Could not inspect drives: " + error.Message; }
        finally { cancellation = null; IsBusy = false; }
    }
    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        IsBusy = true;
        using var source = new CancellationTokenSource(); cancellation = source;
        try
        {
            bool accepting = true;
            var reporter = new Progress<ConversionProgress>(p => { if (accepting) { Progress = p.FilePercent; Status = p.Message; } });
            try
            {
                Result = await new DiscRipService(reader).RunAsync(new(SelectedDrive!, Tracks.Where(t => t.Selected).Select(t => t.Track.Number).ToArray(), Destination, DiscName, (RipFormat)Format, options), policy, reporter, source.Token, resolve);
            }
            finally { accepting = false; }
            Status = $"{(Result.Cancelled ? "Cancelled" : "Finished")}: {Result.Succeeded} succeeded, {Result.Skipped} skipped, {Result.Failed} failed.";
            if (Result.Failed > 0) Status += " " + string.Join("; ", Result.Files.Where(f => f.Status == ConversionStatus.Failed).Select(f => f.Source + ": " + f.Message));
        }
        catch (Exception error) { Status = error.Message; }
        finally { cancellation = null; IsBusy = false; }
    }
    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Cancel() => cancellation?.Cancel();
}
