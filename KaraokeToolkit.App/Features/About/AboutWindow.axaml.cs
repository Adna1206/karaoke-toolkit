using Avalonia.Controls;
using Avalonia.Interactivity;
namespace KaraokeToolkit.App.Features.About;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        DataContext = new AboutViewModel();
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
