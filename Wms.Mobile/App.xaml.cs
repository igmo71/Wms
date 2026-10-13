namespace Wms.Mobile;

public partial class App : Application
{
    private readonly AppShell _appShell;

    public App(AppShell appShell)
    {
        InitializeComponent();
        UserAppTheme = AppTheme.Light;
        _appShell = appShell;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(_appShell);
    }
}
