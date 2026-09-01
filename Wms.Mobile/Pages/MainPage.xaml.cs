using Wms.Contracts.Mobile.V1;
using Wms.Mobile.Services;

namespace Wms.Mobile;

public partial class MainPage : ContentPage
{
    private readonly MobileApiClient _apiClient;
    private readonly IServiceProvider _services;
    private bool _sessionChecked;

    public MainPage(
        MobileApiClient apiClient,
        IServiceProvider services)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _services = services;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_sessionChecked)
        {
            return;
        }

        _sessionChecked = true;
        await RunAsync(() => _apiClient.GetCurrentUserAsync(), ignoreMissingSession: true);
    }

    private async void OnLoginClicked(object? sender, EventArgs e)
    {
        var email = EmailEntry.Text?.Trim() ?? string.Empty;
        var password = PasswordEntry.Text ?? string.Empty;
        PasswordEntry.Text = string.Empty;

        await RunAsync(() => _apiClient.LoginAsync(email, password));
    }

    private void OnLogoutClicked(object? sender, EventArgs e)
    {
        _apiClient.Logout();
        ShowLoggedOut();
        StatusLabel.Text = "Сессия завершена на устройстве.";
    }

    private async void OnInventoryTransferClicked(object? sender, EventArgs e) =>
        await Navigation.PushAsync(_services.GetRequiredService<InventoryTransferPage>());

    private async void OnInventoryCountClicked(object? sender, EventArgs e) =>
        await Navigation.PushAsync(_services.GetRequiredService<InventoryCountPage>());

    private async void OnReceivingOrdersClicked(object? sender, EventArgs e) =>
        await Navigation.PushAsync(_services.GetRequiredService<ReceivingOrderPage>());

    private async void OnShippingOrdersClicked(object? sender, EventArgs e) =>
        await Navigation.PushAsync(_services.GetRequiredService<ShippingOrderPage>());

    private async void OnScannerDiagnosticsClicked(object? sender, EventArgs e) =>
        await Navigation.PushAsync(_services.GetRequiredService<ScannerDiagnosticsPage>());

    private async Task RunAsync(
        Func<Task<MobileCurrentUserResponse>> action,
        bool ignoreMissingSession = false)
    {
        SetBusy(true);
        StatusLabel.Text = string.Empty;

        try
        {
            var user = await action();
            ShowLoggedIn(user);
        }
        catch (MobileApiException exception)
        {
            ShowLoggedOut();
            if (!ignoreMissingSession
                || exception.StatusCode != System.Net.HttpStatusCode.Unauthorized)
            {
                StatusLabel.Text = exception.Message;
            }
        }
        catch (HttpRequestException)
        {
            ShowLoggedOut();
            StatusLabel.Text = "Сервер WMS недоступен.";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ShowLoggedIn(MobileCurrentUserResponse user)
    {
        CurrentUserLabel.Text = $"{user.DisplayName}\n{user.Email}";
        LoginPanel.IsVisible = false;
        SessionPanel.IsVisible = true;
    }

    private void ShowLoggedOut()
    {
        LoginPanel.IsVisible = true;
        SessionPanel.IsVisible = false;
        CurrentUserLabel.Text = string.Empty;
    }

    private void SetBusy(bool isBusy)
    {
        ProgressIndicator.IsVisible = isBusy;
        ProgressIndicator.IsRunning = isBusy;
        LoginButton.IsEnabled = !isBusy;
    }

}
