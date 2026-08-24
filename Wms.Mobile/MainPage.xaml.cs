using BarcodeScanning;
using Wms.Contracts.Mobile.V1;
using Wms.Mobile.Scanning;
using Wms.Mobile.Services;

namespace Wms.Mobile;

public partial class MainPage : ContentPage
{
    private readonly MobileApiClient _apiClient;
    private readonly ILifecycleBarcodeScanner _intentScanner;
    private readonly ICameraBarcodeScanner _cameraScanner;
    private readonly Queue<BarcodeScanEvent> _recentScans = new();
    private bool _scannerSubscribed;
    private bool _sessionChecked;

    public MainPage(
        MobileApiClient apiClient,
        ILifecycleBarcodeScanner intentScanner,
        ICameraBarcodeScanner cameraScanner)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _intentScanner = intentScanner;
        _cameraScanner = cameraScanner;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!_scannerSubscribed)
        {
            _intentScanner.ScanReceived += OnScanReceived;
            _cameraScanner.ScanReceived += OnScanReceived;
            _scannerSubscribed = true;
        }

        if (_sessionChecked)
        {
            return;
        }

        _sessionChecked = true;
        await RunAsync(() => _apiClient.GetCurrentUserAsync(), ignoreMissingSession: true);
    }

    protected override void OnDisappearing()
    {
        StopCamera();
        if (_scannerSubscribed)
        {
            _intentScanner.ScanReceived -= OnScanReceived;
            _cameraScanner.ScanReceived -= OnScanReceived;
            _scannerSubscribed = false;
        }

        base.OnDisappearing();
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
        StopCamera();
        _apiClient.Logout();
        ShowLoggedOut();
        StatusLabel.Text = "Сессия завершена на устройстве.";
    }

    private async void OnCameraClicked(object? sender, EventArgs e)
    {
        if (CameraScannerView.CameraEnabled)
        {
            StopCamera();
            return;
        }

        if (!_cameraScanner.IsAvailable)
        {
            CameraStatusLabel.Text = "На устройстве не обнаружена камера.";
            return;
        }

        var permission = await Permissions.CheckStatusAsync<Permissions.Camera>();
        if (permission != PermissionStatus.Granted)
        {
            permission = await Permissions.RequestAsync<Permissions.Camera>();
        }

        if (permission != PermissionStatus.Granted)
        {
            CameraStatusLabel.Text =
                "Доступ к камере не предоставлен. Встроенный сканер продолжает работать.";
            return;
        }

        CameraScannerView.IsVisible = true;
        CameraScannerView.CameraEnabled = true;
        CameraButton.Text = "Выключить камеру";
        CameraStatusLabel.Text = "Наведите заднюю камеру на штрихкод или QR-код.";
    }

    private void OnCameraDetectionFinished(object? sender, OnDetectionFinishedEventArg e)
    {
        var result = e.BarcodeResults.FirstOrDefault();
        if (!_cameraScanner.TryAccept(result?.DisplayValue, symbology: null))
        {
            return;
        }

        MainThread.BeginInvokeOnMainThread(StopCamera);
    }

    private void OnScanReceived(object? sender, BarcodeScanEvent scanEvent)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _recentScans.Enqueue(scanEvent);
            while (_recentScans.Count > 5)
            {
                _recentScans.Dequeue();
            }

            ScannerStatusLabel.Text =
                $"Получено: {scanEvent.ReceivedAt.ToLocalTime():HH:mm:ss} · {GetSourceName(scanEvent.Source)}";
            ScanHistoryLabel.Text = string.Join(
                Environment.NewLine,
                _recentScans.Reverse().Select(scan =>
                    $"{scan.ReceivedAt.ToLocalTime():HH:mm:ss}  [{scan.Value}]"));
        });
    }

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

    private void StopCamera()
    {
        CameraScannerView.CameraEnabled = false;
        CameraScannerView.IsVisible = false;
        CameraButton.Text = "Включить камеру";
        CameraStatusLabel.Text = "Камера выключена.";
    }

    private static string GetSourceName(BarcodeScanSource source) => source switch
    {
        BarcodeScanSource.EmbeddedScanner => "встроенный сканер",
        BarcodeScanSource.Camera => "камера",
        BarcodeScanSource.KeyboardWedge => "keyboard wedge",
        _ => source.ToString()
    };
}
