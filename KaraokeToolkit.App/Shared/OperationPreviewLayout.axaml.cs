using Avalonia;
using Avalonia.Controls;

namespace KaraokeToolkit.App.Shared;

/// <summary>Places operation-specific controls around a preview and an optional issues pane.</summary>
public sealed partial class OperationPreviewLayout : UserControl
{
    private const double MinimumReadableIssuesHeight = 64;

    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<OperationPreviewLayout, string>(nameof(Title), "");
    public static readonly StyledProperty<string> PreviewTitleProperty =
        AvaloniaProperty.Register<OperationPreviewLayout, string>(nameof(PreviewTitle), "");
    public static readonly StyledProperty<GridLength> SettingsWidthProperty =
        AvaloniaProperty.Register<OperationPreviewLayout, GridLength>(nameof(SettingsWidth), new GridLength(310));
    public static readonly StyledProperty<Control?> HeaderContentProperty =
        AvaloniaProperty.Register<OperationPreviewLayout, Control?>(nameof(HeaderContent));
    public static readonly StyledProperty<Control?> SettingsContentProperty =
        AvaloniaProperty.Register<OperationPreviewLayout, Control?>(nameof(SettingsContent));
    public static readonly StyledProperty<Control?> PreviewContentProperty =
        AvaloniaProperty.Register<OperationPreviewLayout, Control?>(nameof(PreviewContent));
    public static readonly StyledProperty<Control?> PreviewActionsProperty =
        AvaloniaProperty.Register<OperationPreviewLayout, Control?>(nameof(PreviewActions));
    public static readonly StyledProperty<Control?> IssuesContentProperty =
        AvaloniaProperty.Register<OperationPreviewLayout, Control?>(nameof(IssuesContent));
    public static readonly StyledProperty<Control?> FooterActionsProperty =
        AvaloniaProperty.Register<OperationPreviewLayout, Control?>(nameof(FooterActions));
    public static readonly StyledProperty<bool> HasIssuesProperty =
        AvaloniaProperty.Register<OperationPreviewLayout, bool>(nameof(HasIssues));
    public static readonly StyledProperty<string> StatusProperty =
        AvaloniaProperty.Register<OperationPreviewLayout, string>(nameof(Status), "");
    public static readonly StyledProperty<StatusKind> StatusSeverityProperty =
        AvaloniaProperty.Register<OperationPreviewLayout, StatusKind>(nameof(StatusSeverity));
    public static readonly StyledProperty<PreviewProgress?> ProgressProperty =
        AvaloniaProperty.Register<OperationPreviewLayout, PreviewProgress?>(nameof(Progress));

    public string Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string PreviewTitle { get => GetValue(PreviewTitleProperty); set => SetValue(PreviewTitleProperty, value); }
    public GridLength SettingsWidth { get => GetValue(SettingsWidthProperty); set => SetValue(SettingsWidthProperty, value); }
    public Control? HeaderContent { get => GetValue(HeaderContentProperty); set => SetValue(HeaderContentProperty, value); }
    public Control? SettingsContent { get => GetValue(SettingsContentProperty); set => SetValue(SettingsContentProperty, value); }
    public Control? PreviewContent { get => GetValue(PreviewContentProperty); set => SetValue(PreviewContentProperty, value); }
    public Control? PreviewActions { get => GetValue(PreviewActionsProperty); set => SetValue(PreviewActionsProperty, value); }
    public Control? IssuesContent { get => GetValue(IssuesContentProperty); set => SetValue(IssuesContentProperty, value); }
    public Control? FooterActions { get => GetValue(FooterActionsProperty); set => SetValue(FooterActionsProperty, value); }
    public bool HasIssues { get => GetValue(HasIssuesProperty); set => SetValue(HasIssuesProperty, value); }
    public string Status { get => GetValue(StatusProperty); set => SetValue(StatusProperty, value); }
    public StatusKind StatusSeverity { get => GetValue(StatusSeverityProperty); set => SetValue(StatusSeverityProperty, value); }
    public PreviewProgress? Progress { get => GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }

    public OperationPreviewLayout() => InitializeComponent();

    private void IssuesPane_SizeChanged(object? sender, SizeChangedEventArgs args)
    {
        IssuesBody.IsVisible = args.NewSize.Height >= MinimumReadableIssuesHeight;
    }
}
