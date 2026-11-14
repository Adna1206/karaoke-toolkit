using Avalonia.Controls;

namespace KaraokeToolkit.App.Features.Main;

public partial class ActivityLogView : UserControl
{
    private ProgressLogAutoScroll? autoScroll;

    public ActivityLogView()
    {
        InitializeComponent();
        if (Design.IsDesignMode)
        {
            ActivityLog.ItemsSource = new ActivityEntry[]
            {
                new("00:00:00", "Checking grouping plan…"),
                new("00:00:01", "Processing: ABC-12345.zip"),
                new("00:00:02", "Processing: Disc 1/ABC-12346.zip")
            };
            return;
        }
        Loaded += (_, _) => autoScroll ??= new ProgressLogAutoScroll(ActivityLog);
        Unloaded += (_, _) =>
        {
            autoScroll?.Dispose();
            autoScroll = null;
        };
    }
}
