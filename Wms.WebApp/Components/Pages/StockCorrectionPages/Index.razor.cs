using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;
using Wms.Application.Inventory;
using Wms.Application.Warehouses;
using Wms.Common;
using Wms.Data;
using Wms.Domain;

namespace Wms.WebApp.Components.Pages.StockCorrectionPages;

public partial class Index
{
    [Inject] private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = null!;
    [Inject] private CorrectCellStock CorrectCellStock { get; set; } = null!;
    [Inject] private IDialogService DialogService { get; set; } = null!;
    [Inject] private ILogger<Index> Logger { get; set; } = null!;
    [Inject] private StockAvailabilityStore StockAvailability { get; set; } = null!;
    [Inject] private WarehouseService WarehouseService { get; set; } = null!;

    private Warehouse? _warehouse;
    private StorageLocation? _location;
    private CellStockAvailability? _cell;
    private StockKeepingUnit? _sku;
    private DateTime? _expiration;
    private bool _withoutDate;
    private decimal? _quantity;
    private bool _busy;
    private CorrectCellStockRequest? _pending;
    private string? _error;
    private string? _loadError;
    private string? _status;

    private bool InputsLocked => _busy || _pending is not null;
    private DateOnly? Expiration => _expiration is DateTime date ? DateOnly.FromDateTime(date) : null;

    private async Task<IEnumerable<Warehouse>> SearchWarehousesAsync(string? searchText, CancellationToken ct) =>
        (await WarehouseService.ListAsync(new ListQuery
        {
            SearchString = searchText,
            ExcludeDeleted = true,
            SortBy = "Name",
            Take = 10
        }, ct)).Items;

    private async Task<IEnumerable<StorageLocation>> SearchLocationsAsync(string? searchText, CancellationToken ct) =>
        _warehouse is { } warehouse ? await StockAvailability.SearchStorageCellsAsync(warehouse.Id, searchText, ct) : [];

    private async Task<IEnumerable<StockKeepingUnit>> SearchSkusAsync(string? searchText, CancellationToken ct) =>
        await StockAvailability.SearchCorrectionSkusAsync(searchText, ct);

    private void SelectWarehouse(Warehouse? warehouse)
    {
        if (InputsLocked) return;
        _warehouse = warehouse;
        _location = null;
        _cell = null;
        _loadError = null;
        SelectSku(null);
    }

    private async Task SelectLocationAsync(StorageLocation? location)
    {
        if (InputsLocked) return;
        _location = location;
        _cell = null;
        _loadError = null;
        SelectSku(null);
        await ReloadAsync();
    }

    private void SelectSku(StockKeepingUnit? sku)
    {
        if (InputsLocked) return;
        _sku = sku;
        _expiration = null;
        _withoutDate = false;
        _quantity = null;
        _error = null;
        _status = null;
    }

    private void SelectGroup(CellStockGroup group)
    {
        if (InputsLocked) return;
        SelectSku(group.Sku);
        var date = group.Stock.Group.ExpirationDate;
        _expiration = date?.ToDateTime(TimeOnly.MinValue);
        _withoutDate = date is null;
    }

    private bool CanPost(bool adding)
    {
        if (InputsLocked || _cell is not { Location.ActiveLock: null } cell || _sku is not { } sku
            || (_expiration is null && !_withoutDate) || _quantity is not decimal quantity
            || !WarehouseQuantity.IsPositive(quantity))
            return false;
        if (adding)
            return !sku.DeletionMark;

        var group = cell.Groups.FirstOrDefault(x => x.Sku.Id == sku.Id && x.Stock.Group.ExpirationDate == Expiration);
        return group is not null && group.Stock.AvailableQuantity >= quantity;
    }

    private async Task ReloadAsync()
    {
        if (InputsLocked || _location is null) return;
        _busy = true;
        try { await ReadCellAsync(); }
        finally { _busy = false; }
    }

    private async Task ReadCellAsync()
    {
        _loadError = null;
        _cell = null;
        if (_location is not { } location) return;

        try
        {
            var result = await StockAvailability.ReadCorrectionCellAsync(location.WarehouseId, location.Code, default);
            if (result.IsSuccess)
                _cell = result.Value;
            else
                _loadError = result.Error!.Message;
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Failed to read cell {CellId} for stock correction.", location.Id);
            _loadError = "Не удалось загрузить остатки ячейки.";
        }
    }

    private async Task RunCorrectionAsync(bool? adding = null)
    {
        if (_busy || (_pending is null && (adding is not bool direction || !CanPost(direction)))) return;

        _busy = true;
        _error = null;
        _status = null;
        try
        {
            var state = await AuthenticationStateProvider.GetAuthenticationStateAsync();
            var userId = state.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null)
            {
                _error = "Не удалось определить текущего пользователя.";
                return;
            }

            var pending = _pending;
            if (pending is null)
            {
                var location = _cell!.Location;
                var sku = _sku!;
                var expiration = Expiration;
                var quantity = _quantity!.Value;
                var action = adding == true ? "Добавить" : "Списать";
                var message = $"{action} {FormatQuantity(quantity)} {GetUnitText(sku)}. "
                    + $"Ячейка {location.DisplayAddress}. {GetSkuText(sku)} · {FormatExpiration(expiration)}.";
                if (await DialogService.ShowMessageBoxAsync("Подтвердить корректировку", message,
                    yesText: action, cancelText: "Отмена") != true) return;

                pending = new(Guid.NewGuid(), userId, location.WarehouseId, location.Id,
                    sku.Id, expiration, adding == true ? quantity : -quantity);
                _pending = pending;
            }

            if (pending.UserId != userId)
            {
                _error = "Повторите операцию под пользователем, который её начал.";
                return;
            }

            var posted = await CorrectCellStock.ExecuteAsync(pending);
            if (posted.IsSuccess)
            {
                _pending = null;
                _quantity = null;
                _status = $"Корректировка {pending.QuantityChange:+0.000;-0.000} проведена.";
            }
            else
            {
                _error = posted.Error!.Message;
                if (posted.Error.Type != OperationErrorType.Failure)
                    _pending = null;
            }

            if (_pending is null)
                await ReadCellAsync();
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Failed to complete stock correction {RequestId}.", _pending?.RequestId);
            _error = _pending is null ? "Не удалось начать корректировку."
                : "Результат не подтверждён. Повторите отправку этой корректировки.";
        }
        finally { _busy = false; }
    }

    private static string GetLocationText(StorageLocation? location) => location is null ? string.Empty
        : $"{StorageLocationDisplay.Format(location)} · {location.Zone?.Name}";

    private static string GetSkuText(StockKeepingUnit? sku) => sku is null ? string.Empty
        : string.Join(" · ", new[] { sku.Name, sku.Code }.Where(x => !string.IsNullOrWhiteSpace(x)));

    private static string GetUnitText(StockKeepingUnit? sku) => sku is null ? string.Empty
        : sku.BaseUnitOfMeasure?.Description ?? sku.BaseUnitOfMeasure?.Name ?? "ед. неизвестна";

    private static string FormatExpiration(DateOnly? date) => date?.ToString("dd.MM.yyyy") ?? "Без даты";
    private static string FormatQuantity(decimal quantity) => quantity.ToString("0.000");
}
