using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace KaraokeToolkit.App.Features.Main;

public partial class OperationPickerView : UserControl
{
    public static readonly StyledProperty<Control?> SourcePanelProperty =
        AvaloniaProperty.Register<OperationPickerView, Control?>(nameof(SourcePanel));

    public Control? SourcePanel
    {
        get => GetValue(SourcePanelProperty);
        set => SetValue(SourcePanelProperty, value);
    }

    public OperationPickerView()
    {
        InitializeComponent();
        CategoryList.AddHandler(KeyDownEvent, CategoryList_KeyDown, RoutingStrategies.Tunnel);
        OperationList.AddHandler(KeyDownEvent, OperationList_KeyDown, RoutingStrategies.Tunnel);
    }

    internal void FocusFirstCategory()
    {
        CategoryList.SelectedIndex = 0;
        FocusSelection(CategoryList);
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

}
