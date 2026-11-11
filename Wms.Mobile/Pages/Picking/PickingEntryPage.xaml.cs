using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Wms.Contracts.Mobile.V1;
using Wms.Mobile.Scanning;
using Wms.Mobile.Services;

namespace Wms.Mobile;

public partial class PickingEntryPage : ContentPage
{
    private readonly MobilePickingClient _client;
    private readonly IOperationalBarcodeScanner _scanner;
    private readonly IServiceProvider _services;
    private PickingAssemblyProcess _process;
    private MobilePickingAssemblyResponse? _assembly;
    private MobilePickingWorkResponse? _work;
    private MobilePickingSkuResponse? _sku;
    private MobilePickingScannedSourceResponse? _source;
    private MobilePickingWorkEntryResponse? _returnEntry;
    private Guid _skuId;
    private string? _recommendedCode;
    private bool _directPalletPick;
    private bool _palletHeld;
    private bool _returnLocationChecked;
    private bool _needsPalletReturn;
    private bool _visible;
    private bool _busy;
    private long _scanGeneration;
    private PendingAction? _pendingAction;

    public PickingEntryPage(MobilePickingClient client,
        IOperationalBarcodeScanner scanner, PickingAssemblyProcess process,
        IServiceProvider services)
    {
        InitializeComponent();
        _client = client;
        _scanner = scanner;
        _services = services;
        _process = process;
        CameraScannerView.Configure(scanner);
    }

    public void ShowPick(MobilePickingAssemblyResponse assembly,
        PickingAssemblyProcess process, Guid skuId,
        string? recommendedCode, bool directPalletPick)
    {
        _assembly = assembly;
        _process = process;
        _skuId = skuId;
        _recommendedCode = recommendedCode;
        _directPalletPick = directPalletPick;
        _returnEntry = null;
        _source = null;
        _returnLocationChecked = false;
        _needsPalletReturn = false;
        LocationEntry.Text = string.Empty;
        QuantityEntry.Text = string.Empty;
    }

    public void ShowReturn(MobilePickingAssemblyResponse assembly,
        PickingAssemblyProcess process, MobilePickingWorkEntryResponse entry)
    {
        ShowPick(assembly, process, entry.SkuId, entry.LocationCode, false);
        _returnEntry = entry;
        QuantityEntry.Text = entry.Quantity.ToString("0.###", CultureInfo.CurrentCulture);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _visible = true;
        _scanner.ScanReceived += OnScanReceived;
        await RefreshAsync();
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

    private async Task RefreshAsync()
    {
        if (_busy || _assembly is null) return;
        SetBusy(true);
        try
        {
            _assembly = await _client.GetAsync(_assembly.Id, _assembly.WarehouseId);
            _work = await _client.WorkAsync(_assembly.Id, _assembly.WarehouseId);
            if (_returnEntry is null)
                _sku = await _client.SkuAsync(_assembly.Id,
                    _assembly.WarehouseId, _skuId);
            Apply();
            ErrorLabel.Text = string.Empty;
        }
        catch (MobileApiException exception) { ErrorLabel.Text = exception.Message; }
        catch (HttpRequestException) { ErrorLabel.Text = "Не удалось обновить карточку Отбора."; }
        finally { SetBusy(false); }
    }

    private async void OnInfoClicked(object? sender, EventArgs e)
    {
        if (_assembly is null || _skuId == Guid.Empty || _process.HasPendingCommand)
            return;
        var page = _services.GetRequiredService<SkuLocationsPage>();
        page.Show(_assembly.WarehouseId, _skuId, SkuLabel.Text,
            _work?.Skus.FirstOrDefault(x => x.SkuId == _skuId)?.Unit);
        await Navigation.PushAsync(page);
    }

    private void Apply()
    {
        if (_assembly is null) return;
        NumberLabel.Text = $"Сборка {_assembly.Number}";
        var sku = _work?.Skus.FirstOrDefault(x => x.SkuId == _skuId);
        SkuLabel.Text = sku?.Name ?? sku?.Code ?? _skuId.ToString();
        var unit = sku?.Unit ?? "—";
        if (_returnEntry is { } entry)
        {
            Title = "Отбор › Возврат";
            RemainingLabel.Text = $"Вернуть из факта: {entry.Quantity:0.###} {unit}";
            RecommendationLabel.Text = $"Исходная ячейка: {entry.LocationCode}";
            ExpiryLabel.Text = entry.ExpirationDate is DateOnly date
                ? $"Срок: {date:dd.MM.yyyy}" : "Срок не указан";
            StockLabel.Text = _returnLocationChecked
                ? "Подтвердите, что возвращаемый товар находится в исходной ячейке."
                : "Сканируйте исходную ячейку для возврата.";
            ConfirmButton.Text = "Подтвердить возврат";
            ConfirmButton.IsEnabled = !_busy && _returnLocationChecked
                && !_needsPalletReturn;
            ShortageButton.IsVisible = false;
            HoldPalletButton.IsVisible = false;
            ReturnPalletButton.IsVisible = _needsPalletReturn;
        }
        else
        {
            Title = "Отбор › Факт";
            RemainingLabel.Text = $"Осталось: {_sku?.RemainingQuantity ?? 0:0.###} {unit}";
            if (_recommendedCode is null)
                _recommendedCode = _sku?.Locations
                    .Where(x => x.AvailableQuantity > 0)
                    .OrderBy(x => x.ExpirationDate is null)
                    .ThenBy(x => x.ExpirationDate)
                    .ThenBy(x => x.LocationCode)
                    .FirstOrDefault()?.LocationCode;
            var allPicked = _work?.Skus.All(x =>
                x.ConfirmedQuantity >= x.PlannedQuantity) == true;
            RecommendationLabel.Text = _recommendedCode is null
                ? allPicked ? "Все SKU набраны. Проверьте закрытие сборки."
                    : "Доступной ячейки нет. Ожидается Подпитка или решение руководителя."
                : $"Рекомендуемая ячейка: {_recommendedCode}"
                    + (_directPalletPick ? " · снять весь поддон" : "");
            StockLabel.Text = _source is null ? "Сканируйте ячейку."
                : $"Ячейка {_source.LocationCode}: учёт {_source.AccountingQuantity:0.###}, "
                    + $"доступно {_source.AvailableQuantity:0.###}, "
                    + $"резерв {_source.ReservedQuantity:0.###}, "
                    + $"спорное {_source.BlockedQuantity:0.###}";
            ExpiryLabel.Text = _source?.ExpirationDate is DateOnly expiry
                ? $"Срок: {expiry:dd.MM.yyyy}"
                : _source?.RequiresVisualExpiryCheck == true
                    ? "Срок не указан: проверьте его визуально до подтверждения."
                    : _source is null ? string.Empty : "Срок не указан";
            ConfirmButton.Text = "Подтвердить Отбор";
            ConfirmButton.IsEnabled = !_busy && _source is not null
                && (!_directPalletPick || _palletHeld);
            ShortageButton.IsVisible = _source is not null;
            HoldPalletButton.IsVisible = _source is not null
                && _directPalletPick && !_palletHeld;
            ReturnPalletButton.IsVisible = _directPalletPick && _palletHeld
                && _source is not null && _work?.Entries.Any(x =>
                    x.StorageLocationId == _source.StorageLocationId) != true;
        }
    }

    private void OnScanReceived(object? sender, BarcodeScanEvent scan)
    {
        var generation = Volatile.Read(ref _scanGeneration);
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (_visible && generation == Volatile.Read(ref _scanGeneration))
                await CheckLocationAsync(scan.Value);
        });
    }

    private async void OnLocationCompleted(object? sender, EventArgs e) =>
        await CheckLocationAsync(LocationEntry.Text);

    private async void OnCheckLocationClicked(object? sender, EventArgs e) =>
        await CheckLocationAsync(LocationEntry.Text);

    private async Task CheckLocationAsync(string? code)
    {
        if (!_visible || _busy || _process.HasPendingCommand || _assembly is null
            || string.IsNullOrWhiteSpace(code)) return;
        Interlocked.Increment(ref _scanGeneration);
        LocationEntry.Text = code.Trim();
        ErrorLabel.Text = string.Empty;
        if (_returnEntry is not null)
        {
            _returnLocationChecked = true;
            Apply();
            return;
        }
        SetBusy(true);
        try
        {
            _source = await _client.ScanSourceAsync(_assembly.Id,
                new(_assembly.WarehouseId, _skuId, code.Trim()));
            _palletHeld = _sku?.Locations.Any(x =>
                x.StorageLocationId == _source.StorageLocationId && x.Held) == true;
            QuantityEntry.Text = _source.SuggestedQuantity
                .ToString("0.###", CultureInfo.CurrentCulture);
            Apply();
        }
        catch (MobileApiException exception)
        {
            _source = null;
            ErrorLabel.Text = exception.Message;
            Apply();
        }
        catch (HttpRequestException) { ErrorLabel.Text = "Не удалось проверить ячейку."; }
        finally { SetBusy(false); await UpdateCameraAsync(); }
    }

    private async void OnConfirmClicked(object? sender, EventArgs e)
    {
        if (_busy || _process.HasPendingCommand || _assembly is null) return;
        if (!TryQuantity(out var quantity)) return;
        if (_returnEntry is { } entry)
        {
            if (!_returnLocationChecked || quantity > entry.Quantity)
            {
                ErrorLabel.Text = "Укажите количество не больше подтверждённого факта.";
                return;
            }
            if (!await DisplayAlertAsync("Возврат в ячейку",
                $"Подтверждаете, что {quantity:0.###} находится в исходной ячейке {entry.LocationCode}?",
                "Подтвердить", "Отмена")) return;
            await RunActionAsync(PendingAction.Return,
                () => _process.ReducePickAsync(_assembly.Id,
                    _assembly.WarehouseId, entry.Id, _assembly.Revision,
                    LocationEntry.Text ?? "", quantity, null));
        }
        else if (_source is { } source)
        {
            if (quantity > source.SuggestedQuantity)
            {
                ErrorLabel.Text = "Количество превышает предложенное для этой ячейки.";
                return;
            }
            if (_directPalletPick && !_palletHeld)
            {
                ErrorLabel.Text = "Сначала снимите весь поддон и подтвердите удержание.";
                return;
            }
            await RunActionAsync(PendingAction.Pick,
                () => _process.ConfirmPickAsync(_assembly.Id,
                    _assembly.WarehouseId, _assembly.Revision,
                    source.LocationCode, _skuId, source.ExpirationDate, quantity));
        }
    }

    private async void OnShortageClicked(object? sender, EventArgs e)
    {
        if (_busy || _process.HasPendingCommand || _assembly is null
            || _source is not { } source) return;
        if (!TryQuantity(out var found, allowZero: true)
            || found >= source.SuggestedQuantity)
        {
            ErrorLabel.Text = "Для недостачи укажите найденное количество меньше предложенного.";
            return;
        }
        if (!await DisplayAlertAsync("Недостача",
            $"Ожидалось {source.SuggestedQuantity:0.###}, найдено {found:0.###}. Сообщить руководителю?",
            "Сообщить", "Отмена")) return;
        await RunActionAsync(PendingAction.Shortage,
            () => _process.ReportShortageAsync(_assembly.Id,
                _assembly.WarehouseId, _assembly.Revision,
                source.LocationCode, _skuId, source.ExpirationDate,
                source.SuggestedQuantity, found, null));
    }

    private async void OnHoldPalletClicked(object? sender, EventArgs e)
    {
        if (_busy || _process.HasPendingCommand || _assembly is null
            || _source is not { } source) return;
        await RunActionAsync(PendingAction.Hold,
            () => _process.HoldPalletAsync(_assembly.Id, _assembly.WarehouseId,
                _assembly.Revision, source.LocationCode, _skuId));
    }

    private async void OnReturnPalletClicked(object? sender, EventArgs e)
    {
        if (_busy || _process.HasPendingCommand || _assembly is null
            || string.IsNullOrWhiteSpace(LocationEntry.Text)
            || _returnEntry is not null && !_returnLocationChecked) return;
        await RunActionAsync(PendingAction.PalletReturn,
            () => _process.ReturnPalletAsync(_assembly.Id,
                _assembly.WarehouseId, _assembly.Revision,
                LocationEntry.Text ?? ""));
    }

    private bool TryQuantity(out decimal quantity, bool allowZero = false)
    {
        var text = (QuantityEntry.Text ?? "").Trim().Replace(',', '.');
        if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out quantity)
            || !WarehouseQuantityInput.IsSupported(quantity)
            || quantity < 0 || !allowZero && quantity == 0)
        {
            ErrorLabel.Text = "Укажите количество с точностью до трёх знаков после запятой.";
            return false;
        }
        return true;
    }

    private async Task RunActionAsync(PendingAction action,
        Func<Task<MobilePickingAssemblyResponse>> execute, bool retry = false)
    {
        if (_busy || _process.HasPendingCommand && !retry) return;
        _pendingAction = action;
        SetBusy(true);
        ErrorLabel.Text = string.Empty;
        try
        {
            _assembly = await execute();
            _pendingAction = null;
            await AfterActionAsync(action);
        }
        catch (MobileApiException exception)
        {
            _pendingAction = null;
            ErrorLabel.Text = exception.Message;
        }
        catch (HttpRequestException)
        {
            ErrorLabel.Text = _process.HasPendingCommand
                ? "Ответ не подтверждён. Повторите ту же команду."
                : "Действие выполнено, но экран не обновился. Вернитесь к списку и обновите его.";
        }
        finally { SetBusy(false); await UpdateCameraAsync(); }
    }

    private async Task AfterActionAsync(PendingAction action)
    {
        if (_assembly is null) return;
        _work = await _client.WorkAsync(_assembly.Id, _assembly.WarehouseId);
        if (action == PendingAction.Hold)
        {
            _palletHeld = true;
            _sku = await _client.SkuAsync(_assembly.Id, _assembly.WarehouseId, _skuId);
            await CheckLocationAfterHoldAsync();
        }
        else if (action is PendingAction.Pick or PendingAction.Shortage)
        {
            var route = await _client.RouteAsync(_assembly.Id, _assembly.WarehouseId);
            _source = null;
            _palletHeld = false;
            LocationEntry.Text = string.Empty;
            QuantityEntry.Text = string.Empty;
            if (route.Steps.FirstOrDefault() is { } next)
            {
                _skuId = next.SkuId;
                _recommendedCode = next.LocationCode;
                _directPalletPick = next.DirectPalletPick;
                _sku = await _client.SkuAsync(_assembly.Id, _assembly.WarehouseId, _skuId);
            }
            else
            {
                _recommendedCode = null;
                _sku = null;
                if (route.Gaps.FirstOrDefault() is { } gap)
                {
                    _skuId = gap.SkuId;
                    _sku = await _client.SkuAsync(_assembly.Id,
                        _assembly.WarehouseId, _skuId);
                    ErrorLabel.Text = "Оставшийся товар недоступен. Нужна Подпитка или разбор.";
                }
                else
                    ErrorLabel.Text = "Доступные шаги закончились. Проверьте закрытие сборки.";
            }
            Apply();
        }
        else if (action == PendingAction.Return)
        {
            var remaining = _work.Entries.SingleOrDefault(x => x.Id == _returnEntry?.Id);
            if (remaining is null && _returnEntry?.FromReserve == true
                && !_work.Entries.Any(x =>
                    x.StorageLocationId == _returnEntry.StorageLocationId))
            {
                _needsPalletReturn = true;
                Apply();
                ErrorLabel.Text = "Подтвердите возврат поддона в исходную ячейку.";
            }
            else await Navigation.PopAsync();
        }
        else if (action == PendingAction.PalletReturn)
            await Navigation.PopAsync();
    }

    private async Task CheckLocationAfterHoldAsync()
    {
        if (_assembly is null || string.IsNullOrWhiteSpace(LocationEntry.Text)) return;
        _source = await _client.ScanSourceAsync(_assembly.Id,
            new(_assembly.WarehouseId, _skuId, LocationEntry.Text));
        Apply();
    }

    private async void OnRetryClicked(object? sender, EventArgs e)
    {
        if (_busy || !_process.HasPendingCommand || _pendingAction is null) return;
        await RunActionAsync(_pendingAction.Value, _process.RetryAsync, retry: true);
    }

    private async Task UpdateCameraAsync()
    {
        if (_visible && !_busy && !_process.HasPendingCommand
            && _scanner.ActiveSource == BarcodeScanSource.Camera)
            await CameraScannerView.StartAsync();
        else CameraScannerView.Stop();
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        Interlocked.Increment(ref _scanGeneration);
        ProgressIndicator.IsVisible = busy;
        RetryButton.IsVisible = !busy && _process.HasPendingCommand;
        NavigationPage.SetHasBackButton(this, !busy && !_process.HasPendingCommand);
        if (busy) CameraScannerView.Stop();
        Apply();
    }

    protected override bool OnBackButtonPressed()
    {
        if (!_busy && !_process.HasPendingCommand) return base.OnBackButtonPressed();
        ErrorLabel.Text = "Сначала завершите повтор текущей команды.";
        return true;
    }

    private enum PendingAction { Pick, Return, Hold, PalletReturn, Shortage }
}
