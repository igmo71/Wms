using Wms.Contracts.Mobile.V1;
using Wms.Mobile.Services;

namespace Wms.Mobile;

public partial class MainPage : ContentPage
{
    private readonly MobileApiClient _apiClient;
    private readonly IMobileSessionStore _sessionStore;
    private readonly IServiceProvider _services;
    private bool _checkingSession;
    private bool _currentUserLoaded;

    public MainPage(
        MobileApiClient apiClient,
        IServiceProvider services)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _sessionStore = services.GetRequiredService<IMobileSessionStore>();
        _services = services;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_checkingSession)
        {
            return;
        }

        _checkingSession = true;
        try
        {
            var session = await _sessionStore.GetAsync();
            if (session is null)
            {
                ShowLoggedOut();
                return;
            }

            if (!_currentUserLoaded)
            {
                await RestoreCurrentUserAsync();
            }
        }
        finally
        {
            _checkingSession = false;
        }
    }

    private async void OnLoginClicked(object? sender, EventArgs e)
    {
        var email = EmailEntry.Text?.Trim() ?? string.Empty;
        var password = PasswordEntry.Text ?? string.Empty;
        PasswordEntry.Text = string.Empty;

        SetBusy(true);
        StatusLabel.Text = string.Empty;
        try
        {
            ShowLoggedIn(await _apiClient.LoginAsync(email, password));
        }
        catch (MobileApiException exception)
        {
            ShowLoggedOut();
            StatusLabel.Text = exception.Message;
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

    private async Task RestoreCurrentUserAsync()
    {
        SetBusy(true);
        StatusLabel.Text = string.Empty;

        try
        {
            ShowLoggedIn(await _apiClient.GetCurrentUserAsync());
        }
        catch (MobileApiException exception)
        {
            ShowLoggedOut();
            if (exception.StatusCode != System.Net.HttpStatusCode.Unauthorized)
            {
                StatusLabel.Text = exception.Message;
            }
        }
        catch (HttpRequestException)
        {
            ShowSessionUnavailable();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ShowLoggedIn(MobileCurrentUserResponse user)
    {
        _currentUserLoaded = true;
        CurrentUserLabel.Text = user.DisplayName;
        LoginPanel.IsVisible = false;
        SessionPanel.IsVisible = true;
    }

    private void ShowLoggedOut()
    {
        _currentUserLoaded = false;
        LoginPanel.IsVisible = true;
        SessionPanel.IsVisible = false;
        CurrentUserLabel.Text = string.Empty;
    }

    private void ShowSessionUnavailable()
    {
        _currentUserLoaded = false;
        LoginPanel.IsVisible = false;
        SessionPanel.IsVisible = true;
        CurrentUserLabel.Text = "Сессия сохранена";
        StatusLabel.Text = "Сервер WMS недоступен. Повторите действие позже.";
    }

    private void SetBusy(bool isBusy)
    {
        ProgressIndicator.IsVisible = isBusy;
        ProgressIndicator.IsRunning = isBusy;
        LoginButton.IsEnabled = !isBusy;
    }

}
