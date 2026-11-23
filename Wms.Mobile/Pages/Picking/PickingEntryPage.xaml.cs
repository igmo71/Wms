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
    private string? _scannedLocationCode;
    private string? _initialScanCode;
    private PickingEntryOrigin _origin;
    private bool _recommendedPalletPick;
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
        string? recommendedCode, bool directPalletPick, PickingEntryOrigin origin,
        string? initialScanCode = null)
    {
        _assembly = assembly;
        _process = process;
        _skuId = skuId;
        _recommendedCode = recommendedCode;
        _recommendedPalletPick = directPalletPick;
        _origin = origin;
        _initialScanCode = initialScanCode;
        _sku = null;
        _work = null;
        _returnEntry = null;
        _source = null;
        _returnLocationChecked = false;
        _needsPalletReturn = false;
        _scannedLocationCode = null;
        QuantityEntry.Text = string.Empty;
    }

    public void ShowReturn(MobilePickingAssemblyResponse assembly,
        PickingAssemblyProcess process, MobilePickingWorkEntryResponse entry)
    {
        ShowPick(assembly, process, entry.SkuId, entry.LocationCode, false, PickingEntryOrigin.Sku);
        _returnEntry = entry;
        QuantityEntry.Text = entry.Quantity.ToString("0.###", CultureInfo.CurrentCulture);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        using var measurement = MobilePerformanceDiagnostics.Load("Карточка отбора");
        _visible = true;
        _scanner.ScanReceived += OnScanReceived;
        await RefreshAsync();
        if (_visible && _sku is not null && _initialScanCode is { } code)
        {
            _initialScanCode = null;
            await CheckLocationAsync(code);
        }
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
        using var measurement = MobilePerformanceDiagnostics.Load("Карточка отбора");
        SetBusy(true);
        try
        {
            _assembly = await _client.GetAsync(_assembly.Id, _assembly.WarehouseId);
            _work = await _client.WorkAsync(_assembly.Id, _assembly.WarehouseId);
            if (_returnEntry is null)
            {
                _sku = await _client.SkuAsync(_assembly.Id,
                    _assembly.WarehouseId, _skuId);
                if (_origin == PickingEntryOrigin.Sku)
                    SetLocationChoices(_sku);
            }
            Apply();
            ErrorLabel.Text = string.Empty;
        }
        catch (MobileApiException exception) { ErrorLabel.Text = exception.Message; }
        catch (HttpRequestException) { ErrorLabel.Text = "Не удалось обновить карточку Отбора."; }
        finally { SetBusy(false); }
    }

    private async void OnInfoClicked(object? sender, EventArgs e)
    {
        if (_busy || _assembly is null || _skuId == Guid.Empty || _process.HasPendingCommand)
            return;
        var page = _services.GetRequiredService<SkuLocationsPage>();
        page.Show(_assembly.WarehouseId, _skuId, SkuLabel.Text,
            _work?.Skus.FirstOrDefault(x => x.SkuId == _skuId)?.Unit);
        await Navigation.PushAsync(page);
    }

    private void Apply()
    {
        if (_assembly is null) return;
        var sku = _work?.Skus.FirstOrDefault(x => x.SkuId == _skuId);
        SkuLabel.Text = sku?.Name ?? sku?.Code ?? _skuId.ToString();
        var unit = sku?.Unit ?? "—";
        PlanLabel.Text = sku is null ? $"— {unit}" : $"{sku.PlannedQuantity:0.###} {unit}";
        FactLabel.Text = sku is null ? "—" : $"{sku.ConfirmedQuantity:0.###}";
        SkuCard.BackgroundColor = sku is null ? Colors.White
            : sku.ConfirmedQuantity == sku.PlannedQuantity ? Color.FromArgb("#E4F5E7")
            : sku.ConfirmedQuantity > 0 ? Color.FromArgb("#FFF1CF") : Colors.White;
        var canInteract = !_busy && !_process.HasPendingCommand;
        var choosingLocation = _origin == PickingEntryOrigin.Sku && _returnEntry is null;
        RecommendationCard.IsVisible = !choosingLocation;
        LocationChoices.IsVisible = choosingLocation && _source is null;
        LocationChoices.IsEnabled = canInteract;
        QuantityCaptionLabel.IsVisible = !choosingLocation || _source is not null;
        QuantityEntry.IsVisible = QuantityCaptionLabel.IsVisible;
        ConfirmButton.IsVisible = QuantityCaptionLabel.IsVisible;
        InfoButton.IsEnabled = canInteract;
        QuantityEntry.IsEnabled = canInteract && !_needsPalletReturn
            && (_returnEntry is not null || _source is not null);
        HoldPalletButton.IsEnabled = canInteract;
        ReturnPalletButton.IsEnabled = canInteract;
        ShortageHintLabel.IsVisible = _returnEntry is null && _source is not null;
        QuantityCaptionLabel.Text = _returnEntry is null ? "Количество" : "Количество к возврату";
        SourceAddressLabel.IsVisible = _returnEntry is null && _source is not null;
        SourceCaptionLabel.IsVisible = SourceAddressLabel.IsVisible;
        AvailableBlock.IsVisible = SourceAddressLabel.IsVisible;
        AvailableLabel.Text = _source is null ? "—" : $"{_source.AvailableQuantity:0.###}";
        SourceAddressLabel.Text = _source is null ? string.Empty
            : string.IsNullOrWhiteSpace(_source.LocationName) ? _source.LocationCode : _source.LocationName;
        if (_returnEntry is { } entry)
        {
            SourceCaptionLabel.Text = "Считанный код ячейки";
            SourceCaptionLabel.IsVisible = _scannedLocationCode is not null;
            SourceAddressLabel.IsVisible = SourceCaptionLabel.IsVisible;
            SourceAddressLabel.Text = _scannedLocationCode;
            Title = "Отбор › Возврат";
            PickingTitle.Set(HeaderTitleLabel, Title, _assembly.Number);
            Title = $"Отбор › Возврат · {_assembly.Number}";
            RemainingCaptionLabel.Text = "К возврату";
            RemainingLabel.Text = $"{entry.Quantity:0.###}";
            RecommendationCaptionLabel.Text = "Исходная ячейка";
            RecommendationLabel.Text = string.IsNullOrWhiteSpace(entry.LocationName) ? entry.LocationCode : entry.LocationName;
            ExpiryLabel.Text = entry.ExpirationDate is DateOnly date
                ? $"Срок: {date:dd.MM.yyyy}" : "Срок не указан";
            StockLabel.Text = _returnLocationChecked
                ? "Подтвердите, что возвращаемый товар находится в исходной ячейке."
                : "Сканируйте исходную ячейку для возврата.";
            ConfirmButton.Text = "Подтвердить возврат";
            ConfirmButton.IsEnabled = canInteract && _returnLocationChecked
                && !_needsPalletReturn;
            HoldPalletButton.IsVisible = false;
            ReturnPalletButton.IsVisible = _needsPalletReturn;
        }
        else
        {
            SourceCaptionLabel.Text = "Считанная ячейка";
            Title = "Отбор › Факт";
            PickingTitle.Set(HeaderTitleLabel, Title, _assembly.Number);
            Title = $"Отбор › Факт · {_assembly.Number}";
            RemainingCaptionLabel.Text = "Осталось";
            RemainingLabel.Text = sku is null ? "—"
                : $"{Math.Max(0, sku.PlannedQuantity - sku.ConfirmedQuantity):0.###}";
            var allPicked = _work?.Skus.All(x =>
                x.ConfirmedQuantity >= x.PlannedQuantity) == true;
            var recommendedName = _sku?.Locations.FirstOrDefault(x =>
                x.LocationCode == _recommendedCode)?.LocationName;
            RecommendationCaptionLabel.Text = _recommendedCode is null ? "Следующий шаг" : "Рекомендуемая ячейка";
            RecommendationLabel.Text = _recommendedCode is null
                ? allPicked ? "Все SKU набраны. Проверьте закрытие сборки."
                    : "Доступной ячейки нет. Ожидается Подпитка или решение руководителя."
                : (string.IsNullOrWhiteSpace(recommendedName) ? _recommendedCode : recommendedName);
            RecommendationLabel.FontSize = _recommendedCode is null ? 13 : 22;
            if (_recommendedPalletPick && _recommendedCode is not null)
                RecommendationCaptionLabel.Text += " · снять весь поддон";
            StockLabel.Text = _source is null
                ? choosingLocation && _sku?.Locations.Any(IsOfferedForSelection) == true
                    ? "Выберите или сканируйте ячейку."
                    : "Сканируйте ячейку."
                : $"Остаток {_source.AccountingQuantity:0.###}, резерв {_source.ReservedQuantity:0.###}, "
                    + $"спорное {_source.BlockedQuantity:0.###}";
            ExpiryLabel.Text = _source?.ExpirationDate is DateOnly expiry
                ? $"Срок: {expiry:dd.MM.yyyy}"
                : _source?.RequiresVisualExpiryCheck == true
                    ? "Срок не указан: проверьте его визуально до подтверждения."
                    : _source is null ? string.Empty : "Срок не указан";
            ConfirmButton.Text = "Подтвердить Отбор";
            ConfirmButton.IsEnabled = canInteract && _source is { } source
                && (!source.FromReserve || source.PalletHeld);
            HoldPalletButton.IsVisible = _source is { FromReserve: true, PalletHeld: false };
            ReturnPalletButton.IsVisible = _source is { FromReserve: true, PalletHeld: true } pallet
                && _work?.Entries.Any(x => x.StorageLocationId == pallet.StorageLocationId) != true;
        }
    }

    private void SetLocationChoices(MobilePickingSkuResponse sku) =>
        BindableLayout.SetItemsSource(LocationChoices, sku.Locations
            .Where(IsOfferedForSelection)
            .Select(x => new PickingLocationChoice(x, sku.RemainingQuantity)).ToArray());

    private static bool IsOfferedForSelection(MobilePickingAvailabilityResponse location) =>
        location.OfferedForSelection ?? (!location.FromReserve || location.CanPickNow);

    private async void OnLocationTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is PickingLocationChoice choice)
            await CheckLocationAsync(choice.Location.LocationCode, choice.Location);
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

    private async Task CheckLocationAsync(string? code, MobilePickingAvailabilityResponse? choice = null)
    {
        if (!_visible || _busy || _process.HasPendingCommand || _assembly is null
            || string.IsNullOrWhiteSpace(code)) return;
        Interlocked.Increment(ref _scanGeneration);
        _scannedLocationCode = code.Trim();
        ErrorLabel.Text = string.Empty;
        if (_returnEntry is not null)
        {
            _returnLocationChecked = true;
            Apply();
            return;
        }
        using var measurement = MobilePerformanceDiagnostics.Load("Проверка ячейки");
        SetBusy(true);
        _source = null;
        QuantityEntry.Text = string.Empty;
        try
        {
            _source = await _client.ScanSourceAsync(_assembly.Id,
                new(_assembly.WarehouseId, _skuId, _scannedLocationCode,
                    ExpirationDate: choice?.ExpirationDate, DateGroupSelected: choice is not null));
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
        if (!TryQuantity(out var quantity, allowZero: _returnEntry is null)) return;
        if (_returnEntry is { } entry)
        {
            if (!_returnLocationChecked || quantity > entry.Quantity)
            {
                ErrorLabel.Text = "Укажите количество не больше подтверждённого факта.";
                return;
            }
            if (!await DisplayAlertAsync("Возврат в ячейку",
                $"Подтверждаете, что {quantity:0.###} находится в исходной ячейке {(string.IsNullOrWhiteSpace(entry.LocationName) ? entry.LocationCode : entry.LocationName)}?",
                "Подтвердить", "Отмена")) return;
            await RunActionAsync(PendingAction.Return,
                () => _process.ReducePickAsync(_assembly.Id,
                    _assembly.WarehouseId, entry.Id, _assembly.Revision,
                    _scannedLocationCode ?? "", quantity, null));
        }
        else if (_source is { } source)
        {
            if (quantity > source.SuggestedQuantity)
            {
                ErrorLabel.Text = "Количество превышает предложенное для этой ячейки.";
                return;
            }
            if (source.FromReserve && !source.PalletHeld)
            {
                ErrorLabel.Text = "Сначала снимите весь поддон и подтвердите удержание.";
                return;
            }
            if (quantity < source.SuggestedQuantity)
            {
                var address = string.IsNullOrWhiteSpace(source.LocationName)
                    ? source.LocationCode : source.LocationName;
                var missing = source.AvailableQuantity - quantity;
                var unit = _work?.Skus.FirstOrDefault(x => x.SkuId == _skuId)?.Unit ?? "ед.";
                if (!await DisplayAlertAsync("Недостача в ячейке",
                    $"В ячейке {address} предложено отобрать {source.SuggestedQuantity:0.###} {unit}. "
                    + $"Свободно по учёту {source.AvailableQuantity:0.###} {unit}; фактически в ячейке {quantity:0.###} {unit}. "
                    + $"Отобрать найденное и заблокировать недостачу {missing:0.###} {unit} до решения руководителя?",
                    "Подтвердить недостачу", "Отмена")) return;
                await RunActionAsync(PendingAction.Shortage,
                    () => _process.ReportShortageAsync(_assembly.Id,
                        _assembly.WarehouseId, _assembly.Revision,
                        source.LocationCode, _skuId, source.ExpirationDate,
                        source.AvailableQuantity, quantity, null));
            }
            else
                await RunActionAsync(PendingAction.Pick,
                    () => _process.ConfirmPickAsync(_assembly.Id,
                        _assembly.WarehouseId, _assembly.Revision,
                        source.LocationCode, _skuId, source.ExpirationDate, quantity));
        }
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
            || string.IsNullOrWhiteSpace(_scannedLocationCode)
            || _returnEntry is not null && !_returnLocationChecked) return;
        await RunActionAsync(PendingAction.PalletReturn,
            () => _process.ReturnPalletAsync(_assembly.Id,
                _assembly.WarehouseId, _assembly.Revision,
                _scannedLocationCode ?? ""));
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
        using var measurement = MobilePerformanceDiagnostics.Action(action switch
        {
            PendingAction.Pick => "Подтверждение отбора",
            PendingAction.Return => "Возврат товара",
            PendingAction.Hold => "Снятие поддона",
            PendingAction.PalletReturn => "Возврат поддона",
            PendingAction.Shortage => "Сообщение о нехватке",
            _ => "Команда отбора"
        });
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
        if (_origin == PickingEntryOrigin.Sku
            && action is (PendingAction.Pick or PendingAction.Shortage))
        {
            await ReturnAfterActionAsync();
            return;
        }
        if (action == PendingAction.Hold)
        {
            var source = _source;
            _source = null;
            QuantityEntry.Text = string.Empty;
            _work = await _client.WorkAsync(_assembly.Id, _assembly.WarehouseId);
            _sku = await _client.SkuAsync(_assembly.Id, _assembly.WarehouseId, _skuId);
            if (_origin == PickingEntryOrigin.Sku)
                SetLocationChoices(_sku);
            if (source is not null)
            {
                _source = await _client.ScanSourceAsync(_assembly.Id,
                    new(_assembly.WarehouseId, _skuId, source.LocationCode,
                        ExpirationDate: source.ExpirationDate, DateGroupSelected: true));
                QuantityEntry.Text = _source.SuggestedQuantity.ToString("0.###", CultureInfo.CurrentCulture);
            }
        }
        else if (action is PendingAction.Pick or PendingAction.Shortage)
        {
            var route = await _client.RouteAsync(_assembly.Id, _assembly.WarehouseId);
            _work = route.Work;
            _source = null;
            _scannedLocationCode = null;
            QuantityEntry.Text = string.Empty;
            if (route.Steps.FirstOrDefault() is { } next)
            {
                _skuId = next.SkuId;
                _recommendedCode = next.LocationCode;
                _recommendedPalletPick = next.DirectPalletPick;
                _sku = await _client.SkuAsync(_assembly.Id, _assembly.WarehouseId, _skuId);
            }
            else
            {
                var previous = Navigation.NavigationStack.Count >= 2
                    ? Navigation.NavigationStack[^2] as PickingRoutePage : null;
                await PickingSectionNavigation.SwitchAsync(this, _services, _assembly,
                    _process, PickingSection.Sku);
                if (previous is not null)
                    Navigation.RemovePage(previous);
                return;
            }
            Apply();
        }
        else if (action == PendingAction.Return)
        {
            _work = await _client.WorkAsync(_assembly.Id, _assembly.WarehouseId);
            var remaining = _work.Entries.SingleOrDefault(x => x.Id == _returnEntry?.Id);
            if (remaining is null && _returnEntry?.FromReserve == true
                && !_work.Entries.Any(x =>
                    x.StorageLocationId == _returnEntry.StorageLocationId))
            {
                _needsPalletReturn = true;
                Apply();
                ErrorLabel.Text = "Подтвердите возврат поддона в исходную ячейку.";
            }
            else await ReturnAfterActionAsync();
        }
        else if (action == PendingAction.PalletReturn)
            await ReturnAfterActionAsync();
    }

    private async Task ReturnAfterActionAsync()
    {
        if (_assembly is not null && Navigation.NavigationStack.Count >= 2
            && Navigation.NavigationStack[^2] is PickingSkuPage skuPage)
            skuPage.UseFreshAssemblyOnReturn(_assembly);
        await Navigation.PopAsync();
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
        ProgressIndicator.IsRunning = busy;
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

public enum PickingEntryOrigin { Sku, Route }

public sealed record PickingLocationChoice(MobilePickingAvailabilityResponse Location,
    decimal RemainingQuantity)
{
    public string Address => string.IsNullOrWhiteSpace(Location.LocationName)
        ? Location.LocationCode : Location.LocationName;
    public string Expiry => Location.ExpirationDate is DateOnly date
        ? date.ToString("dd.MM.yyyy", CultureInfo.CurrentCulture) : "Срок не указан";
    public string Stock => $"Остаток {Location.AccountingQuantity:0.###}";
    public string Available => $"Доступно {Location.AvailableQuantity:0.###}";
    public bool HasExpiredStock => Location.HasExpiredStock;
    public string ZoneIcon => Location.FromReserve ? "picking_pallet.png" : "picking_shelf.png";
    public string ZoneName => Location.FromReserve ? "Хранение" : "Комплектация";
    public Color AvailableColor => Location.CanPickNow && Location.AvailableQuantity < RemainingQuantity
        ? Colors.DarkRed : Color.FromArgb("#202124");
}
