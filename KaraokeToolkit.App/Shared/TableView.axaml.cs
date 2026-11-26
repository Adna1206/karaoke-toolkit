using Avalonia;
using Avalonia.Controls;

namespace KaraokeToolkit.App.Shared;

/// <summary>Shared table frame and row styling for lists and trees.</summary>
public sealed partial class TableView : UserControl
{
    public static readonly StyledProperty<Control?> HeaderProperty =
        AvaloniaProperty.Register<TableView, Control?>(nameof(Header));
    public static readonly StyledProperty<Control?> RowsProperty =
        AvaloniaProperty.Register<TableView, Control?>(nameof(Rows));

    public Control? Header { get => GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }
    public Control? Rows { get => GetValue(RowsProperty); set => SetValue(RowsProperty, value); }

    public TableView() => InitializeComponent();
}
