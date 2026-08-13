using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Wms.Application.Services;
using Wms.Application.Services.Inventory;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.WebApp.Components.Pages.InventoryTransferPages;

public partial class Work
{
    [Parameter] public Guid? Id { get; set; }

    [Inject] private InventoryTransferQueryService InventoryTransferQueryService { get; set; } = null!;
    [Inject] private InventoryTransferCommandService InventoryTransferCommandService { get; set; } = null!;
    [Inject] private WarehouseService WarehouseService { get; set; } = null!;
    [Inject] private StorageLocationService StorageLocationService { get; set; } = null!;
    [Inject] private StockKeepingUnitService StockKeepingUnitService { get; set; } = null!;
    [Inject] private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = null!;
    [Inject] private NavigationManager NavigationManager { get; set; } = null!;

    private InventoryTransfer? _transfer;
    private Warehouse? _warehouse;
    private StorageLocation? _transitStorageLocation;
    private List<InventoryTransferTransitBalance> _transitBalances = [];
    private List<InventoryMovement> _movements = [];

    private StorageLocation? _pickSource;
    private StockKeepingUnit? _pickSku;
    private double _pickQuantity;
    private Guid? _putSkuId;
    private StorageLocation? _putDestination;
    private double _putQuantity;
    private StorageLocation? _directSource;
    private StorageLocation? _directDestination;
    private StockKeepingUnit? _directSku;
    private double _directQuantity;

    private bool _isLoading = true;
    private bool _isSaving;
    private bool _operationFailed;
    private string? _errorMessage;

    private string Title => _transfer is null ? "Новое перемещение" : $"Перемещение №{_transfer.Number}";
    private bool IsStarted => _transfer is not null;
    private bool CanEdit => _transfer is not null && _transfer.Status != InventoryTransferStatus.Completed;
    private bool CanUseTransit => CanEdit && _transfer!.TransitStorageLocationId.HasValue;
    private bool CanPutAnything => CanUseTransit && _transitBalances.Count > 0;
    private bool CanDelete => _transfer?.Status == InventoryTransferStatus.Draft;
    private bool CanComplete => _transfer?.Status == InventoryTransferStatus.InProgress && _transitBalances.Count == 0;
    private bool CanPick => CanUseTransit && _pickSource is not null && _pickSku is not null && _pickQuantity > 0;
    private bool CanPut => CanPutAnything && _putSkuId.HasValue && _putDestination is not null && _putQuantity > 0;
    private bool CanMoveDirect => CanEdit && _directSource is not null && _directDestination is not null
        && _directSource.Id != _directDestination.Id && _directSku is not null && _directQuantity > 0;

    protected override async Task OnParametersSetAsync()
    {
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        _isLoading = true;

        if (Id is Guid id)
        {
            _transfer = await InventoryTransferQueryService.GetAsync(id);
            _warehouse = _transfer?.Warehouse;
            _transitStorageLocation = _transfer?.TransitStorageLocation;
            _movements = _transfer is null ? [] : await InventoryTransferQueryService.GetMovementsAsync(id);
            _transitBalances = _transfer is null ? [] : await InventoryTransferQueryService.GetTransitBalancesAsync(id);

            if (_putSkuId.HasValue && _transitBalances.All(x => x.StockKeepingUnit.Id != _putSkuId.Value))
                _putSkuId = null;
        }
        else
        {
            _transfer = null;
            _warehouse = null;
            _transitStorageLocation = null;
            _movements = [];
            _transitBalances = [];
        }

        _isLoading = false;
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

    private async Task<IEnumerable<StorageLocation>> SearchTransitStorageLocationsAsync(string? searchText, CancellationToken ct)
    {
        if (_transfer is null)
            return [];

        var result = await StorageLocationService.ListAsync(new StorageLocationListQuery
        {
            SearchString = searchText,
            WarehouseId = _transfer.WarehouseId,
            ZoneType = ZoneType.Transit,
            SortBy = "Name",
            Take = 10
        }, ct);
        return result.Items;
    }

    private async Task<IEnumerable<StorageLocation>> SearchStorageLocationsAsync(string? searchText, CancellationToken ct)
    {
        if (_transfer is null)
            return [];

        var result = await StorageLocationService.ListAsync(new StorageLocationListQuery
        {
            SearchString = searchText,
            WarehouseId = _transfer.WarehouseId,
            ZoneType = ZoneType.Storage,
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

    private async Task StartAsync()
    {
        if (_warehouse is null)
            return;

        await RunAsync(async userId =>
        {
            var result = await InventoryTransferCommandService.CreateAsync(_warehouse.Id, userId);
            if (!result.IsSuccess || result.Value is null)
            {
                SetError(result.Error?.Message ?? "Не удалось создать перемещение.");
                return;
            }

            NavigationManager.NavigateTo($"inventory-transfers/{result.Value.Id}");
        });
    }

    private async Task SetTransitStorageLocationAsync(StorageLocation? location)
    {
        if (_transfer is null)
            return;

        var previous = _transitStorageLocation;
        _transitStorageLocation = location;
        await RunAsync(async _ =>
        {
            var result = await InventoryTransferCommandService.SetTransitStorageLocationAsync(_transfer.Id, location?.Id);
            if (!result.IsSuccess)
            {
                _transitStorageLocation = previous;
                SetError(result.Error?.Message ?? "Не удалось выбрать тележку.");
                return;
            }
            await ReloadAsync();
        }, requiresUser: false);
    }

    private Task PickAsync() => RunAsync(async userId =>
    {
        var result = await InventoryTransferCommandService.PickAsync(
            _transfer!.Id, _pickSource!.Id, _pickSku!.Id, _pickQuantity, userId);
        if (!result.IsSuccess)
        {
            SetError(result.Error?.Message ?? "Не удалось выполнить отбор.");
            return;
        }
        _pickSource = null;
        _pickSku = null;
        _pickQuantity = 0;
        await ReloadAsync();
    });

    private Task PutAsync() => RunAsync(async userId =>
    {
        var result = await InventoryTransferCommandService.PutAsync(
            _transfer!.Id, _putDestination!.Id, _putSkuId!.Value, _putQuantity, userId);
        if (!result.IsSuccess)
        {
            SetError(result.Error?.Message ?? "Не удалось выполнить размещение.");
            return;
        }
        _putDestination = null;
        _putQuantity = 0;
        await ReloadAsync();
    });

    private Task MoveDirectAsync() => RunAsync(async userId =>
    {
        var result = await InventoryTransferCommandService.MoveDirectAsync(
            _transfer!.Id, _directSource!.Id, _directDestination!.Id, _directSku!.Id, _directQuantity, userId);
        if (!result.IsSuccess)
        {
            SetError(result.Error?.Message ?? "Не удалось выполнить прямое перемещение.");
            return;
        }
        _directSource = null;
        _directDestination = null;
        _directSku = null;
        _directQuantity = 0;
        await ReloadAsync();
    });

    private Task CompleteAsync() => RunAsync(async userId =>
    {
        var result = await InventoryTransferCommandService.CompleteAsync(_transfer!.Id, userId);
        if (!result.IsSuccess)
        {
            SetError(result.Error?.Message ?? "Не удалось завершить перемещение.");
            return;
        }
        await ReloadAsync();
    });

    private async Task DeleteAsync()
    {
        if (_transfer is null)
            return;

        await RunAsync(async _ =>
        {
            var result = await InventoryTransferCommandService.DeleteDraftAsync(_transfer.Id);
            if (!result.IsSuccess)
            {
                SetError(result.Error?.Message ?? "Не удалось удалить перемещение.");
                return;
            }
            NavigationManager.NavigateTo("inventory-transfers");
        }, requiresUser: false);
    }

    private async Task RunAsync(Func<string, Task> action, bool requiresUser = true)
    {
        _isSaving = true;
        _operationFailed = false;

        try
        {
            var userId = requiresUser ? await GetCurrentUserIdAsync() : string.Empty;
            if (requiresUser && userId is null)
            {
                SetError("Не удалось определить текущего пользователя.");
                return;
            }
            await action(userId ?? string.Empty);
        }
        catch
        {
            SetError("Не удалось выполнить операцию перемещения.");
        }
        finally
        {
            _isSaving = false;
        }
    }

    private async Task<string?> GetCurrentUserIdAsync()
    {
        var authenticationState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
        return authenticationState.User.FindFirstValue(ClaimTypes.NameIdentifier);
    }

    private void SetError(string message)
    {
        _operationFailed = true;
        _errorMessage = message;
    }

    private static string FormatDateTimeOffset(DateTimeOffset? value) =>
        value?.ToLocalTime().ToString("dd.MM.yyyy HH:mm") ?? "—";

    private static string FormatQuantity(double value) => value.ToString("0.###");
}
