using Wms.Mobile.Services;

namespace Wms.Mobile;

public sealed class PerformanceDiagnosticsView : VerticalStackLayout
{
    static PerformanceDiagnosticsView()
    {
        MobilePerformanceDiagnostics.SetDispatcher(update =>
        {
            if (MainThread.IsMainThread) update();
            else MainThread.BeginInvokeOnMainThread(update);
        });
    }

    public PerformanceDiagnosticsView()
    {
        IsVisible = MobilePerformanceDiagnostics.Enabled;
        if (!IsVisible) return;

        Spacing = 1;
        var load = CreateLabel();
        load.SetBinding(Label.TextProperty, new Binding(
            nameof(MobilePerformanceDiagnostics.LoadText), source: MobilePerformanceDiagnostics.Instance));
        var action = CreateLabel();
        action.SetBinding(Label.TextProperty, new Binding(
            nameof(MobilePerformanceDiagnostics.ActionText), source: MobilePerformanceDiagnostics.Instance));
        action.SetBinding(IsVisibleProperty, new Binding(
            nameof(MobilePerformanceDiagnostics.HasAction), source: MobilePerformanceDiagnostics.Instance));
        Children.Add(load);
        Children.Add(action);

        var toggle = new Button
        {
            FontSize = 11,
            Padding = 0,
            HeightRequest = 28,
            MinimumHeightRequest = 28,
            HorizontalOptions = LayoutOptions.Start,
            BackgroundColor = Colors.Transparent
        };
        toggle.SetAppThemeColor(Button.TextColorProperty, Colors.DimGray, Colors.LightGray);
        toggle.SetBinding(Button.TextProperty, new Binding(
            nameof(MobilePerformanceDiagnostics.RequestsToggleText), source: MobilePerformanceDiagnostics.Instance));
        toggle.Clicked += (_, _) => MobilePerformanceDiagnostics.Instance.ToggleRequests();
        Children.Add(toggle);

        var requests = CreateLabel();
        requests.LineBreakMode = LineBreakMode.CharacterWrap;
        requests.SetBinding(Label.TextProperty, new Binding(
            nameof(MobilePerformanceDiagnostics.RequestsText), source: MobilePerformanceDiagnostics.Instance));
        var details = new ScrollView { Content = requests, MaximumHeightRequest = 140 };
        details.SetBinding(IsVisibleProperty, new Binding(
            nameof(MobilePerformanceDiagnostics.ShowRequests), source: MobilePerformanceDiagnostics.Instance));
        Children.Add(details);
    }

    private static Label CreateLabel()
    {
        var label = new Label { FontSize = 11 };
        label.SetAppThemeColor(Label.TextColorProperty, Colors.DimGray, Colors.LightGray);
        return label;
    }
}
