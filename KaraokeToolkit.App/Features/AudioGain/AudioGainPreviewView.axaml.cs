using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using KaraokeToolkit.Core.Audio;

namespace KaraokeToolkit.App.Features.AudioGain;

public sealed partial class AudioGainPreviewView : UserControl
{
    private readonly TaskCompletionSource<AudioGainPlan?> decision = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Func<bool, bool, Task<bool>>? confirmBack;
    private readonly Func<Task<string?>>? chooseFolder;
    private AudioGainViewModel Model => (AudioGainViewModel)DataContext!;

    public AudioGainPreviewView() => InitializeComponent();

    internal AudioGainPreviewView(AudioGainViewModel model, Func<bool, bool, Task<bool>> confirmBack,
        Func<Task<string?>> chooseFolder) : this()
    {
        DataContext = model;
        this.confirmBack = confirmBack;
        this.chooseFolder = chooseFolder;
    }

    internal void CancelPreview() => decision.TrySetResult(null);

    internal async Task<AudioGainPlan?> ReviewAsync()
    {
        _ = Model.ScanCommand.ExecuteAsync(null);
        try { return await decision.Task; }
        finally { Model.ScanCommand.Cancel(); }
    }

    private async void Back_Click(object? sender, RoutedEventArgs args)
    {
        if (confirmBack == null || await confirmBack(Model.IsScanning, false)) CancelPreview();
    }

    private async void Browse_Click(object? sender, RoutedEventArgs args)
    {
        if (chooseFolder != null && await chooseFolder() is { } path) Model.OutputFolder = path;
    }

    private void Apply_Click(object? sender, RoutedEventArgs args)
    {
        if (Model.CanApply) decision.TrySetResult(Model.Plan);
    }
}
