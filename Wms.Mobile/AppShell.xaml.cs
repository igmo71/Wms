namespace Wms.Mobile;

public partial class AppShell : Shell
{
    public AppShell(MainPage mainPage)
    {
        InitializeComponent();
        MainContent.Content = mainPage;
    }
}
