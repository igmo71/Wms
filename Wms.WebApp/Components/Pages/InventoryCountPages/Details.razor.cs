using Microsoft.AspNetCore.Components;
using Wms.Application.Services;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.WebApp.Components.Pages.InventoryCountPages;

public partial class Details
{
    [Parameter]
    public Guid Id { get; set; }

    [Inject] private InventoryCountQueryService InventoryCountQueryService { get; set; } = null!;
    [Inject] private InventoryCountCommandService InventoryCountCommandService { get; set; } = null!;
    [Inject] private StorageLocationService StorageLocationService { get; set; } = null!;
    [Inject] private StockKeepingUnitService StockKeepingUnitService { get; set; } = null!;

    private InventoryCount? _inventoryCount;
    private bool _isLoading = true;
    private bool _isAddingItem;
    private bool _isPosting;
    private bool _operationFailed;
    private string? _errorMessage;

    private bool IsDraft => _inventoryCount?.Status == InventoryCountStatus.Draft;

    protected override async Task OnParametersSetAsync()
    {
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        _isLoading = true;
        _inventoryCount = await InventoryCountQueryService.GetAsync(Id);
        _isLoading = false;
    }

    private async Task<IEnumerable<StorageLocation>> SearchStorageLocationsAsync(string? searchText, CancellationToken ct)
    {
        if (_inventoryCount is null)
            return [];

        var result = await StorageLocationService.ListAsync(new StorageLocationListQuery
        {
            SearchString = searchText,
            WarehouseId = _inventoryCount.WarehouseId,
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

    private Task UpdateStorageLocationAsync(InventoryCountItem item, StorageLocation? storageLocation)
    {
        return UpdateItemAsync(item, storageLocation?.Id, item.StockKeepingUnitId, item.CountedQuantity);
    }

    private Task UpdateStockKeepingUnitAsync(InventoryCountItem item, StockKeepingUnit? stockKeepingUnit)
    {
        return UpdateItemAsync(item, item.StorageLocationId, stockKeepingUnit?.Id, item.CountedQuantity);
    }

    private Task UpdateCountedQuantityAsync(InventoryCountItem item, double countedQuantity)
    {
        return UpdateItemAsync(item, item.StorageLocationId, item.StockKeepingUnitId, countedQuantity);
    }

    private async Task UpdateItemAsync(
        InventoryCountItem item,
        Guid? storageLocationId,
        Guid? stockKeepingUnitId,
        double countedQuantity)
    {
        _operationFailed = false;

        try
        {
            var result = await InventoryCountCommandService.UpdateItemAsync(
                item.Id,
                storageLocationId,
                stockKeepingUnitId,
                countedQuantity);

            if (!result.IsSuccess)
            {
                _operationFailed = true;
                _errorMessage = result.Error?.Message ?? "Не удалось обновить строку инвентаризации.";
                return;
            }

            await ReloadAsync();
        }
        catch
        {
            _operationFailed = true;
            _errorMessage = "Не удалось обновить строку инвентаризации.";
        }
    }

    private async Task AddItemAsync()
    {
        _isAddingItem = true;
        _operationFailed = false;

        try
        {
            var result = await InventoryCountCommandService.AddItemAsync(Id);
            if (!result.IsSuccess)
            {
                _operationFailed = true;
                _errorMessage = result.Error?.Message ?? "Не удалось добавить строку инвентаризации.";
                return;
            }

            await ReloadAsync();
        }
        catch
        {
            _operationFailed = true;
            _errorMessage = "Не удалось добавить строку инвентаризации.";
        }
        finally
        {
            _isAddingItem = false;
        }
    }

    private async Task DeleteItemAsync(InventoryCountItem item)
    {
        _operationFailed = false;

        try
        {
            var result = await InventoryCountCommandService.DeleteItemAsync(item.Id);
            if (!result.IsSuccess)
            {
                _operationFailed = true;
                _errorMessage = result.Error?.Message ?? "Не удалось удалить строку инвентаризации.";
                return;
            }

            await ReloadAsync();
        }
        catch
        {
            _operationFailed = true;
            _errorMessage = "Не удалось удалить строку инвентаризации.";
        }
    }

    private async Task PostAsync()
    {
        _isPosting = true;
        _operationFailed = false;

        try
        {
            var result = await InventoryCountCommandService.PostAsync(Id);
            if (!result.IsSuccess)
            {
                _operationFailed = true;
                _errorMessage = result.Error?.Message ?? "Не удалось провести инвентаризацию.";
                return;
            }

            await ReloadAsync();
        }
        catch
        {
            _operationFailed = true;
            _errorMessage = "Не удалось провести инвентаризацию.";
        }
        finally
        {
            _isPosting = false;
        }
    }

    private static string FormatDateTimeOffset(DateTimeOffset? value) =>
        value?.ToLocalTime().ToString("dd.MM.yyyy HH:mm") ?? "—";
}
