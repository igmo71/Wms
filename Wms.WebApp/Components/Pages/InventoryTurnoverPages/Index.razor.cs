using Microsoft.AspNetCore.Components;
using MudBlazor;
using Wms.Application.Inventory.Turnovers;
using Wms.Application.StockKeepingUnits;
using Wms.Application.StorageLocations;
using Wms.Application.Warehouses;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.WebApp.Components.Pages.InventoryTurnoverPages;

public partial class Index
{
    [Inject] private InventoryTurnoverQueryService InventoryTurnoverQueryService { get; set; } = null!;
    [Inject] private WarehouseService WarehouseService { get; set; } = null!;
    [Inject] private StorageLocationQueryService StorageLocationQueryService { get; set; } = null!;
    [Inject] private StockKeepingUnitService StockKeepingUnitService { get; set; } = null!;

    private MudDataGrid<InventoryTurnoverListItem> _dataGrid = null!;
    private DateTime? _dateFrom = DateTime.Today;
    private DateTime? _dateTo = DateTime.Today;
    private string? _documentSearchString;
    private Warehouse? _warehouse;
    private StorageLocation? _storageLocation;
    private StockKeepingUnit? _stockKeepingUnit;

    private async Task<GridData<InventoryTurnoverListItem>> LoadServerDataAsync(
        GridState<InventoryTurnoverListItem> state,
        CancellationToken cancellationToken)
    {
        var sortDefinition = state.SortDefinitions.FirstOrDefault();
        var result = await InventoryTurnoverQueryService.ListAsync(new InventoryTurnoverListQuery
        {
            DateFrom = _dateFrom,
            DateTo = _dateTo,
            DocumentSearchString = _documentSearchString,
            WarehouseId = _warehouse?.Id,
            StorageLocationId = _storageLocation?.Id,
            StockKeepingUnitId = _stockKeepingUnit?.Id,
            SortBy = sortDefinition?.SortBy,
            SortDescending = sortDefinition?.Descending ?? true,
            Skip = state.Page * state.PageSize,
            Take = state.PageSize
        }, cancellationToken);

        return new GridData<InventoryTurnoverListItem>
        {
            Items = result.Items,
            TotalItems = result.TotalItems
        };
    }

    private async Task<IEnumerable<Warehouse>> SearchWarehousesAsync(string? searchText, CancellationToken ct)
    {
        var result = await WarehouseService.ListAsync(new ListQuery
        {
            SearchString = searchText,
            SortBy = "Name",
            Take = 10
        }, ct);

        return result.Items;
    }

    private async Task<IEnumerable<StorageLocation>> SearchStorageLocationsAsync(string? searchText, CancellationToken ct)
    {
        var result = await StorageLocationQueryService.ListAsync(new StorageLocationListQuery
        {
            SearchString = searchText,
            WarehouseId = _warehouse?.Id,
            SortBy = "Name",
            Take = 10
        }, ct);

        return result.Items;
    }

    private async Task<IEnumerable<StockKeepingUnit>> SearchStockKeepingUnitsAsync(string? searchText, CancellationToken ct)
    {
        var result = await StockKeepingUnitService.ListAsync(new ListQuery
        {
            SearchString = searchText,
            SortBy = "Name",
            Take = 10
        }, ct);

        return result.Items;
    }

    private Task OnDateFromChangedAsync(DateTime? dateFrom)
    {
        _dateFrom = dateFrom;
        return _dataGrid.ReloadServerData();
    }

    private Task OnDocumentSearchChangedAsync(string? documentSearchString)
    {
        _documentSearchString = documentSearchString;
        return _dataGrid.ReloadServerData();
    }

    private Task OnDateToChangedAsync(DateTime? dateTo)
    {
        _dateTo = dateTo;
        return _dataGrid.ReloadServerData();
    }

    private Task OnWarehouseChangedAsync(Warehouse? warehouse)
    {
        _warehouse = warehouse;
        _storageLocation = null;
        return _dataGrid.ReloadServerData();
    }

    private Task OnStorageLocationChangedAsync(StorageLocation? storageLocation)
    {
        _storageLocation = storageLocation;
        if (storageLocation?.Warehouse is not null)
            _warehouse = storageLocation.Warehouse;

        return _dataGrid.ReloadServerData();
    }

    private Task OnStockKeepingUnitChangedAsync(StockKeepingUnit? stockKeepingUnit)
    {
        _stockKeepingUnit = stockKeepingUnit;
        return _dataGrid.ReloadServerData();
    }

    private static string? GetRecorderHref(InventoryTurnoverListItem item)
    {
        if (item.RecorderId is not Guid recorderId)
            return null;

        return item.RecorderType switch
        {
            RecorderType.ReceivingOrder => $"receiving-orders/{recorderId}",
            RecorderType.ShippingOrder => $"shipping-orders/{recorderId}",
            RecorderType.InventoryCount => $"inventory-counts/{recorderId}",
            RecorderType.InventoryTransfer => $"inventory-transfers/{recorderId}",
            _ => null
        };
    }

    private static string GetRecorderText(InventoryTurnoverListItem item) =>
        item.RecorderNumber is not null && item.RecorderDate is DateTime recorderDate
            ? $"{item.RecorderType.GetDisplayName()} №{item.RecorderNumber} от {recorderDate:dd.MM.yyyy}"
            : item.RecorderType.GetDisplayName();
}
