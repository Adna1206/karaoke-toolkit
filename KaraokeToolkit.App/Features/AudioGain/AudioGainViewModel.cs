using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KaraokeToolkit.App.Shared;
using KaraokeToolkit.Core.Audio;
using KaraokeToolkit.Core.Conversion;

namespace KaraokeToolkit.App.Features.AudioGain;

public sealed record GainModeChoice(AudioGainMode Mode, string Label)
{
    public override string ToString() => Label;
}

public sealed record GainPreviewRow(AudioGainRow Row, bool ShowPeak)
{
    public string FileName => Path.GetFileName(Row.Source);
    public string Current => Row.SourceDuration is TimeSpan duration ? TimeSpan.FromSeconds(Math.Round(duration.TotalSeconds)).ToString(@"hh\:mm\:ss") :
        ShowPeak ? Level(Row.CurrentPeakDbtp, "dBTP") : Level(Row.CurrentLufs, "LUFS");
    public string Estimated => Row.Semitones != null ? "100%" :
        ShowPeak ? Level(Row.EstimatedPeakDbtp, "dBTP") : Level(Row.EstimatedLufs, "LUFS");
    public string Change => Row.Semitones is int shift ? $"{shift:+0;-0;0} st" :
        Row.GainDb is double gain ? $"{gain:+0.00;-0.00;0.00} dB" : "—";
    private static string Level(double? level, string unit) => level is double value ? $"{value:0.00} {unit}" : "—";
}

public sealed partial class AudioGainViewModel(string[] inputs, bool recurse, string outputFolder, bool isKeyChange = false) : ObservableObject
{
    public IReadOnlyList<GainModeChoice> Modes { get; } =
    [
        new(AudioGainMode.Loudness, "Match loudness (LUFS)"),
        new(AudioGainMode.Peak, "Normalize true peak (dBTP)"),
        new(AudioGainMode.Offset, "Adjust by dB")
    ];
    public IReadOnlyList<OverwritePolicy> OverwritePolicies { get; } = [OverwritePolicy.Fail, OverwritePolicy.Skip, OverwritePolicy.Replace];
    public PreviewProgress PreviewProgress { get; } = new();
    private readonly AudioGainService service = new();
    private AudioGainScan? scan;
    internal AudioGainPlan? Plan { get; private set; }
    [ObservableProperty] public partial GainModeChoice? SelectedMode { get; set; }
    [ObservableProperty] public partial OverwritePolicy Overwrite { get; set; } = OverwritePolicy.Fail;
    [ObservableProperty] public partial decimal? TargetLufs { get; set; } = -16;
    [ObservableProperty] public partial decimal? PeakCeiling { get; set; } = -1;
    [ObservableProperty] public partial decimal? OffsetDb { get; set; } = 0;
    [ObservableProperty] public partial decimal? Semitones { get; set; } = 0;
    [ObservableProperty] public partial string OutputFolder { get; set; } = outputFolder;
    [ObservableProperty] public partial string Status { get; set; } = "Measuring audio…";
    [ObservableProperty] public partial StatusKind StatusSeverity { get; set; } = StatusKind.Info;
    [ObservableProperty] public partial IReadOnlyList<GainPreviewRow> Rows { get; private set; } = [];
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanScan)), NotifyPropertyChangedFor(nameof(CanApply)),
        NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    public partial bool IsScanning { get; private set; }
    public bool CanScan => !IsScanning;
    public bool CanApply => !IsScanning && Rows.Any(r => r.Row.Status is AudioGainStatus.Ready or AudioGainStatus.Limited);
    public bool IsKeyChange { get; } = isKeyChange;
    public bool IsLoudness => !IsKeyChange && SelectedMode?.Mode is null or AudioGainMode.Loudness;
    public bool IsOffset => !IsKeyChange && SelectedMode?.Mode == AudioGainMode.Offset;
    public string PeakLabel => SelectedMode?.Mode == AudioGainMode.Peak ? "Target true peak (dBTP)" : "True-peak ceiling (dBTP)";
    public string Title => IsKeyChange ? "Change Audio Key" : "Normalize Audio Gain";
    public string PreviewTitle => IsKeyChange ? "Key change preview" : "Gain preview";
    public string ApplyLabel => IsKeyChange ? "Apply Key Change" : "Apply Gain";
    public string CurrentLabel => IsKeyChange ? "Duration" : "Current level";
    public string EstimatedLabel => IsKeyChange ? "Tempo" : "Est. output";
    public string ChangeLabel => IsKeyChange ? "Key shift" : "Gain";
    public string Help => IsKeyChange
        ? "A negative value lowers the key. Tempo is preserved. Adjusted copies keep any matching CDG files."
        : "Saves adjusted copies. Karaoke ZIPs stay zipped and CDG files are preserved. Changing MP3, AAC, WMA or Vorbis audio re-encodes it.";

    partial void OnSelectedModeChanged(GainModeChoice? value)
    {
        OnPropertyChanged(nameof(IsLoudness));
        OnPropertyChanged(nameof(IsOffset));
        OnPropertyChanged(nameof(PeakLabel));
        UpdatePlan();
    }
    partial void OnTargetLufsChanged(decimal? value) => UpdatePlan();
    partial void OnPeakCeilingChanged(decimal? value) => UpdatePlan();
    partial void OnOffsetDbChanged(decimal? value) => UpdatePlan();
    partial void OnSemitonesChanged(decimal? value) => UpdatePlan();
    partial void OnOverwriteChanged(OverwritePolicy value) => UpdatePlan();
    partial void OnOutputFolderChanged(string value) => UpdatePlan();

    [RelayCommand(CanExecute = nameof(CanScan))]
    internal async Task ScanAsync(CancellationToken token)
    {
        SelectedMode ??= Modes[0];
        IsScanning = true;
        StatusSeverity = StatusKind.Info;
        scan = null;
        Plan = null;
        Rows = [];
        var report = PreviewProgress.Begin(message => Status = message);
        var progress = new Progress<ConversionProgress>(item => report("Measuring audio", item.Completed, item.Found));
        try
        {
            scan = await Task.Run(() => service.ScanAsync(inputs, recurse, progress, token), token);
            UpdatePlan();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            Status = error.Message;
            StatusSeverity = StatusKind.Failure;
        }
        finally
        {
            PreviewProgress.Finish();
            IsScanning = false;
        }
    }

    private void UpdatePlan()
    {
        if (scan == null) return;
        Plan = null;
        Rows = [];
        try
        {
            if (IsKeyChange ? Semitones == null : PeakCeiling == null || IsLoudness && TargetLufs == null || IsOffset && OffsetDb == null)
                throw new ArgumentException("Enter a value for each active audio setting.");
            if (IsKeyChange)
            {
                decimal shift = Semitones!.Value;
                if (shift != decimal.Truncate(shift)) throw new ArgumentException("Enter a whole number of semitones.");
                Plan = service.PlanKey(scan, new() { Semitones = (int)shift }, OutputFolder, Overwrite);
            }
            else
            {
                var options = new AudioGainOptions
                {
                    Mode = SelectedMode?.Mode ?? AudioGainMode.Loudness, TargetLufs = (double)(TargetLufs ?? -16),
                    PeakCeilingDbtp = (double)PeakCeiling!.Value, OffsetDb = (double)(OffsetDb ?? 0)
                };
                Plan = service.Plan(scan, options, OutputFolder, Overwrite);
            }
            Rows = Plan.Rows.Select(row => new GainPreviewRow(row, !IsLoudness)).ToArray();
            int ready = Rows.Count(r => r.Row.Status is AudioGainStatus.Ready or AudioGainStatus.Limited);
            int limited = Rows.Count(r => r.Row.Status == AudioGainStatus.Limited);
            int unavailable = Rows.Count - ready;
            Status = Rows.Count == 0 ? "No supported audio files found." : IsKeyChange
                ? $"{ready:N0} ready · {unavailable:N0} skipped or blocked"
                : $"{ready:N0} ready · {limited:N0} peak-limited · {unavailable:N0} skipped or blocked";
            StatusSeverity = unavailable != 0 || limited != 0 || Rows.Count == 0 ? StatusKind.Warning : StatusKind.Info;
        }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException)
        {
            Status = error.Message;
            StatusSeverity = StatusKind.Failure;
        }
        OnPropertyChanged(nameof(CanApply));
    }
}
