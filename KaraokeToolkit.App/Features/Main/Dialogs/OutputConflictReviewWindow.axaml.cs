using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using KaraokeToolkit.App.Shared;
using KaraokeToolkit.Core.Conversion;

namespace KaraokeToolkit.App.Features.Main.Dialogs;

public sealed partial class OutputConflictReviewWindow : Window
{
    private OutputConflictReviewModel Model => (OutputConflictReviewModel)DataContext!;

    public OutputConflictReviewWindow() => InitializeComponent();

    internal static async Task<IReadOnlyList<ConflictDecision>?> ShowAsync(Window owner,
        IReadOnlyList<OutputConflict> conflicts, CancellationToken token)
    {
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            token.ThrowIfCancellationRequested();
            var window = new OutputConflictReviewWindow { DataContext = new OutputConflictReviewModel(conflicts) };
            window.Opened += (_, _) => window.SkipAll.Focus();
            using var registration = token.Register(() => Dispatcher.UIThread.Post(() => window.Close()));
            return await ModalDialog.ShowAsync<IReadOnlyList<ConflictDecision>?>(window, owner);
        });
    }

    private void SkipAll_Click(object? sender, RoutedEventArgs args) => Model.SetAll(false);
    private void ReplaceAll_Click(object? sender, RoutedEventArgs args) => Model.SetAll(true);
    private void Cancel_Click(object? sender, RoutedEventArgs args) => Close();
    private void Apply_Click(object? sender, RoutedEventArgs args) => Close(Model.GetChoices());
}
