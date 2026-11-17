using Avalonia;
using Avalonia.Controls;

namespace KaraokeToolkit.App.Shared;

public enum StatusKind { Info, Success, Warning, Failure }

public sealed partial class StatusBar : UserControl
{
    public static readonly StyledProperty<string?> MessageProperty =
        AvaloniaProperty.Register<StatusBar, string?>(nameof(Message));
    public static readonly StyledProperty<StatusKind> KindProperty =
        AvaloniaProperty.Register<StatusBar, StatusKind>(nameof(Kind));
    public static readonly StyledProperty<double> MaxMessageHeightProperty =
        AvaloniaProperty.Register<StatusBar, double>(nameof(MaxMessageHeight), double.PositiveInfinity);

    public StatusBar() => InitializeComponent();

    public string? Message { get => GetValue(MessageProperty); set => SetValue(MessageProperty, value); }
    public StatusKind Kind { get => GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public double MaxMessageHeight { get => GetValue(MaxMessageHeightProperty); set => SetValue(MaxMessageHeightProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != KindProperty)
            return;

        PseudoClasses.Set(":success", Kind == StatusKind.Success);
        PseudoClasses.Set(":warning", Kind == StatusKind.Warning);
        PseudoClasses.Set(":failure", Kind == StatusKind.Failure);
    }
}
