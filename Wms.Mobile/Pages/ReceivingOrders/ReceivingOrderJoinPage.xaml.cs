using Wms.Contracts.Mobile.V1;
using Wms.Mobile.Scanning;
using Wms.Mobile.Services;

namespace Wms.Mobile;

public partial class ReceivingOrderJoinPage : ContentPage
{
    private ReceivingOrderReceivingProcess _process;
    private readonly MobileReceivingOrderClient _orders;
    private readonly MobileReferenceDataClient _reference;
    private readonly IOperationalBarcodeScanner _scanner;
    private MobileReceivingWorkOrderResponse? _order;
    private bool _visible;
    private bool _busy;
    private long _scanGeneration;

    public ReceivingOrderJoinPage(ReceivingOrderReceivingProcess process,
        MobileReceivingOrderClient orders, MobileReferenceDataClient reference,
        IOperationalBarcodeScanner scanner)
    {
        InitializeComponent();
        _process = process;
        _orders = orders;
        _reference = reference;
        _scanner = scanner;
        BindingContext = this;
        CameraScannerView.Configure(scanner);
    }

    public IReadOnlyList<ReceivingWorkLocationRow> LocationRows { get; private set; } = [];

    public void Show(MobileReceivingWorkOrderResponse order,
        ReceivingOrderReceivingProcess process)
    {
        _order = order;
        _process = process;
        NumberLabel.Text = order.Number;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _visible = true;
        _scanner.ScanReceived += OnScanReceived;
        await LoadAsync();
        await UpdateCameraAsync();
    }

    protected override void OnDisappearing()
    {
        _visible = false;
        Interlocked.Increment(ref _scanGeneration);
        _scanner.ScanReceived -= OnScanReceived;
        CameraScannerView.Stop();
        base.OnDisappearing();
    }

    private async Task LoadAsync()
    {
        if (_order is null || _busy || _process.HasPendingCommand) return;
        SetBusy(true);
        try
        {
            _order = await _orders.GetAsync(_order.Id);
            NumberLabel.Text = _order.Number;
            ErrorLabel.Text = string.Empty;
        }
        catch (MobileApiException ex) { ErrorLabel.Text = ex.Message; return; }
        catch (HttpRequestException) { ErrorLabel.Text = "Не удалось обновить ордер."; return; }
        finally { SetBusy(false); }

        if (_order.IsParticipant)
        {
            await Navigation.PopAsync();
            return;
        }

        if (_order.Status == MobileReceivingOrderStatus.InReceiving)
        {
            HintLabel.Text = "Присоединение к текущей приёмке.";
            await JoinAsync(null);
        }
        else if (_order.Status == MobileReceivingOrderStatus.ReadyForReceiving)
        {
            HintLabel.Text = "Выберите место приёмки сканированием или поиском.";
            LocationPanel.IsVisible = true;
            await SearchLocationsAsync();
        }
        else
        {
            HintLabel.Text = "В текущем статусе присоединение недоступно.";
        }
    }

    private async void OnLocationSearch(object? sender, EventArgs e) =>
        await SearchLocationsAsync();

    private async Task SearchLocationsAsync()
    {
        if (_order is null || _busy || _process.HasPendingCommand) return;
        SetBusy(true);
        try
        {
            var result = await _reference.FindReceivingLocationsAsync(
                _order.WarehouseId, LocationSearch.Text);
            LocationRows = result.Locations.Select(x => new ReceivingWorkLocationRow(
                x.Id, $"{x.Address} · {x.Name}")).ToArray();
            OnPropertyChanged(nameof(LocationRows));
            ErrorLabel.Text = string.Empty;
        }
        catch (MobileApiException ex) { ErrorLabel.Text = ex.Message; }
        catch (HttpRequestException) { ErrorLabel.Text = "Не удалось найти места приёмки."; }
        finally { SetBusy(false); await UpdateCameraAsync(); }
    }

    private void OnScanReceived(object? sender, BarcodeScanEvent scan)
    {
        var generation = Volatile.Read(ref _scanGeneration);
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (_visible && generation == Volatile.Read(ref _scanGeneration))
                await ResolveLocationAsync(scan.Value);
        });
    }

    private async Task ResolveLocationAsync(string barcode)
    {
        if (!_visible || _order is null || _busy || _process.HasPendingCommand
            || !LocationPanel.IsVisible) return;
        SetBusy(true);
        try
        {
            var location = await _reference.ResolveStorageLocationAsync(
                barcode, _order.WarehouseId, MobileStorageLocationContext.Receiving);
            SetBusy(false);
            if (await DisplayAlertAsync("Место приёмки",
                $"Начать в {location.Address}?", "Да", "Нет"))
                await JoinAsync(location.Id);
        }
        catch (MobileApiException ex) { ErrorLabel.Text = ex.Message; }
        catch (HttpRequestException) { ErrorLabel.Text = "Не удалось проверить место приёмки."; }
        finally { SetBusy(false); await UpdateCameraAsync(); }
    }

    private async void OnLocationTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is Guid id) await JoinAsync(id);
    }

    private async void OnJoinExistingClicked(object? sender, EventArgs e) =>
        await JoinAsync(null);

    private async Task JoinAsync(Guid? locationId)
    {
        if (_order is null || _busy || _process.HasPendingCommand) return;
        SetBusy(true);
        ErrorLabel.Text = string.Empty;
        var success = false;
        try
        {
            _order = await _process.JoinAsync(_order.Id, locationId);
            success = true;
        }
        catch (MobileApiException ex) { ErrorLabel.Text = ex.Message; }
        catch (HttpRequestException)
        {
            ErrorLabel.Text = "Ответ не подтверждён. Повторите ту же команду.";
        }
        catch (InvalidOperationException ex) { ErrorLabel.Text = ex.Message; }
        finally { SetBusy(false); await UpdateCameraAsync(); }
        if (success) await Navigation.PopAsync();
    }

    private async void OnRetryClicked(object? sender, EventArgs e)
    {
        if (_busy || !_process.HasPendingCommand) return;
        SetBusy(true);
        var success = false;
        try
        {
            _order = await _process.RetryAsync();
            ErrorLabel.Text = string.Empty;
            success = true;
        }
        catch (MobileApiException ex) { ErrorLabel.Text = ex.Message; }
        catch (HttpRequestException)
        {
            ErrorLabel.Text = "Ответ не подтверждён. Повторите ту же команду.";
        }
        finally { SetBusy(false); await UpdateCameraAsync(); }
        if (success) await Navigation.PopAsync();
    }

    private void UpdateRecovery()
    {
        RetryButton.IsVisible = _process.HasPendingCommand;
        RetryButton.Text = $"Повторить: {_process.PendingLabel}";
        JoinExistingButton.IsVisible = _order is { Status: MobileReceivingOrderStatus.InReceiving,
            IsParticipant: false } && !_process.HasPendingCommand;
        JoinExistingButton.IsEnabled = !_busy;
        LocationPanel.IsEnabled = !_busy && !_process.HasPendingCommand;
        NavigationPage.SetHasBackButton(this, !_busy && !_process.HasPendingCommand);
    }

    protected override bool OnBackButtonPressed()
    {
        if (!_busy && !_process.HasPendingCommand) return base.OnBackButtonPressed();
        ErrorLabel.Text = "Сначала завершите повтор команды.";
        return true;
    }

    private void SetBusy(bool busy)
    {
        Interlocked.Increment(ref _scanGeneration);
        _busy = busy;
        ProgressIndicator.IsVisible = busy;
        UpdateRecovery();
    }

    private async Task UpdateCameraAsync()
    {
        if (_visible && !_busy && !_process.HasPendingCommand
            && LocationPanel.IsVisible && _scanner.ActiveSource == BarcodeScanSource.Camera)
            await CameraScannerView.StartAsync();
        else CameraScannerView.Stop();
    }
}

public sealed record ReceivingWorkLocationRow(Guid Id, string Display);
