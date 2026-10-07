using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Wms.Application.Commands;
using Wms.Application.Inventory.Transfers;
using Wms.Application.StockKeepingUnits;
using Wms.Application.StorageLocations;
using Wms.Application.Users;
using Wms.Application.Warehouses;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.WebApp.Components.Pages.InventoryTransferPages;

public partial class Work
{
    [Parameter] public Guid? Id { get; set; }

    [Inject] private InventoryTransferQueryService InventoryTransferQueryService { get; set; } = null!;
    [Inject] private ApplicationUserQueryService ApplicationUserQueryService { get; set; } = null!;
    [Inject] private InventoryTransferCommandService InventoryTransferCommandService { get; set; } = null!;
    [Inject] private WarehouseService WarehouseService { get; set; } = null!;
    [Inject] private StorageLocationQueryService StorageLocationQueryService { get; set; } = null!;
    [Inject] private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = null!;
    [Inject] private NavigationManager NavigationManager { get; set; } = null!;

    private InventoryTransfer? _transfer;
    private Warehouse? _warehouse;
    private StorageLocation? _transitStorageLocation;
    private List<InventoryTransferTransitBalance> _transitBalances = [];
    private List<InventoryMovement> _movements = [];

    private StorageLocation? _pickSource;
    private List<InventoryTransferStorageLocationBalance> _pickSourceBalances = [];
    private Guid? _pickSkuId;
    private decimal _pickQuantity;
    private Guid? _putSkuId;
    private StorageLocation? _putDestination;
    private decimal _putQuantity;
    private StorageLocation? _directSource;
    private List<InventoryTransferStorageLocationBalance> _directSourceBalances = [];
    private StorageLocation? _directDestination;
    private Guid? _directSkuId;
    private decimal _directQuantity;

    private bool _isLoading = true;
    private bool _isSaving;
    private PendingTransferOperation? _pendingOperation;
    private bool InputsLocked => _isSaving || _pendingOperation is not null;
    private bool _operationFailed;
    private bool _noAvailableTransitStorageLocations;
    private string? _errorMessage;
    private IReadOnlyDictionary<string, string> _userNames = new Dictionary<string, string>();

    private string Title => _transfer is null
        ? "Новое перемещение"
        : $"Перемещение №{_transfer.Number} от {_transfer.CreatedAtUtc.ToLocalTime():dd.MM.yyyy HH:mm}";
    private bool IsStarted => _transfer is not null;
    private bool HasTransit => _transitStorageLocation is not null;
    private bool CanEdit => _transfer is not null && _transfer.Status != InventoryTransferStatus.Completed;
    private bool CanUseTransit => CanEdit && _transfer!.TransitStorageLocationId.HasValue;
    private bool CanPutAnything => CanUseTransit && _transitBalances.Count > 0;
    private bool CanDelete => _transfer?.Status == InventoryTransferStatus.Draft;
    private bool CanComplete => _transfer?.Status == InventoryTransferStatus.InProgress && _transitBalances.Count == 0;
    private bool ShowComplete => _transfer is not null && _transfer.Status != InventoryTransferStatus.Completed;
    private string CompleteTooltip => _transfer?.Status == InventoryTransferStatus.Draft
        ? "Сначала выполните хотя бы одно перемещение."
        : HasTransit && _transitBalances.Count > 0
            ? "Чтобы завершить, освободите тележку."
            : "Завершить перемещение.";
    private bool CanPick => CanUseTransit && _pickSource is not null && _pickSkuId.HasValue && _pickQuantity > 0;
    private bool CanPut => CanPutAnything && _putSkuId.HasValue && _putDestination is not null && _putQuantity > 0;
    private bool CanMoveDirect => CanEdit && _directSource is not null && _directDestination is not null
        && _directSource.Id != _directDestination.Id && _directSkuId.HasValue && _directQuantity > 0;

    protected override async Task OnParametersSetAsync()
    {
        if (_pendingOperation is { } pending && pending.PageId != Id)
            _pendingOperation = null;
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
            _userNames = _transfer is null
                ? new Dictionary<string, string>()
                : await ApplicationUserQueryService.GetUserNamesAsync([
                    _transfer.CreatedBy,
                    _transfer.StartedBy,
                    _transfer.CompletedBy]);

            if (_transfer?.Status == InventoryTransferStatus.Completed)
            {
                NavigationManager.NavigateTo($"inventory-transfers/{id}");
                return;
            }

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
            _pickSourceBalances = [];
            _directSourceBalances = [];
            _userNames = new Dictionary<string, string>();
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
        if (_warehouse is null)
            return [];

        var items = await InventoryTransferQueryService.GetAvailableTransitStorageLocationsAsync(
            _warehouse.Id, searchText, ct);

        _noAvailableTransitStorageLocations = items.Count == 0 && string.IsNullOrWhiteSpace(searchText);
        return items;
    }

    private async Task<IEnumerable<StorageLocation>> SearchStorageLocationsAsync(string? searchText, CancellationToken ct)
    {
        if (_transfer is null)
            return [];

        var result = await StorageLocationQueryService.ListAsync(new StorageLocationListQuery
        {
            SearchString = searchText,
            WarehouseId = _transfer.WarehouseId,
            ZoneType = ZoneType.Storage,
            ExcludeLocked = true,
            SortBy = "Name",
            Take = 10
        }, ct);
        return result.Items;
    }

    private async Task SetPickSourceAsync(StorageLocation? location)
    {
        _pickSource = location;
        _pickSkuId = null;
        _pickSourceBalances = location is null
            ? []
            : await InventoryTransferQueryService.GetStorageLocationBalancesAsync(location.Id);
    }

    private async Task SetWarehouseAsync(Warehouse? warehouse)
    {
        _warehouse = warehouse;
        _transitStorageLocation = null;
        _noAvailableTransitStorageLocations = false;

        if (warehouse is not null)
        {
            var transitStorageLocations = await InventoryTransferQueryService
                .GetAvailableTransitStorageLocationsAsync(warehouse.Id, null);
            _noAvailableTransitStorageLocations = transitStorageLocations.Count == 0;
        }
    }

    private async Task SetDirectSourceAsync(StorageLocation? location)
    {
        _directSource = location;
        _directSkuId = null;
        _directSourceBalances = location is null
            ? []
            : await InventoryTransferQueryService.GetStorageLocationBalancesAsync(location.Id);
    }

    private Task StartAsync()
    {
        if (InputsLocked || _warehouse is null)
            return Task.CompletedTask;
        var command = new CreateInventoryTransferCommand(_warehouse.Id, _transitStorageLocation?.Id);
        return RunAsync("Создать перемещение",
            context => InventoryTransferCommandService.CreateAsync(command, context),
            transferId =>
            {
                NavigationManager.NavigateTo($"inventory-transfers/{transferId}/work");
                return Task.CompletedTask;
            });
    }

    private Task PickAsync()
    {
        if (InputsLocked || !CanPick)
            return Task.CompletedTask;
        var command = new PickInventoryTransferCommand(_transfer!.Id, _pickSource!.Id, _pickSkuId!.Value, _pickQuantity);
        return RunAsync("Отобрать на тележку",
            context => InventoryTransferCommandService.PickAsync(command, context),
            async _ =>
            {
                _pickSkuId = null;
                _pickQuantity = 0;
                _pickSourceBalances = await InventoryTransferQueryService.GetStorageLocationBalancesAsync(command.SourceStorageLocationId);
                await ReloadAsync();
            });
    }

    private Task PutAsync()
    {
        if (InputsLocked || !CanPut)
            return Task.CompletedTask;
        var command = new PutInventoryTransferCommand(_transfer!.Id, _putDestination!.Id, _putSkuId!.Value, _putQuantity);
        return RunAsync("Разместить с тележки",
            context => InventoryTransferCommandService.PutAsync(command, context),
            async _ =>
            {
                _putDestination = null;
                _putQuantity = 0;
                await ReloadAsync();
            });
    }

    private Task MoveDirectAsync()
    {
        if (InputsLocked || !CanMoveDirect)
            return Task.CompletedTask;
        var command = new MoveDirectInventoryTransferCommand(_transfer!.Id, _directSource!.Id,
            _directDestination!.Id, _directSkuId!.Value, _directQuantity);
        return RunAsync("Выполнить прямое перемещение",
            context => InventoryTransferCommandService.MoveDirectAsync(command, context),
            async _ =>
            {
                _directDestination = null;
                _directSkuId = null;
                _directQuantity = 0;
                _directSourceBalances = await InventoryTransferQueryService.GetStorageLocationBalancesAsync(command.SourceStorageLocationId);
                await ReloadAsync();
            });
    }

    private Task CompleteAsync()
    {
        if (InputsLocked || !CanComplete)
            return Task.CompletedTask;
        var transferId = _transfer!.Id;
        return RunAsync("Завершить перемещение",
            context => InventoryTransferCommandService.CompleteAsync(transferId, context),
            completedId =>
            {
                NavigationManager.NavigateTo($"inventory-transfers/{completedId}");
                return Task.CompletedTask;
            });
    }

    private Task DeleteAsync()
    {
        if (InputsLocked || !CanDelete)
            return Task.CompletedTask;
        var transferId = _transfer!.Id;
        return RunAsync("Удалить черновик",
            context => InventoryTransferCommandService.DeleteDraftAsync(transferId, context),
            _ =>
            {
                NavigationManager.NavigateTo("inventory-transfers");
                return Task.CompletedTask;
            });
    }

    private async Task RunAsync(
        string label,
        Func<CommandContext, Task<OperationResult<Guid>>> execute,
        Func<Guid, Task> onSuccess)
    {
        if (InputsLocked)
            return;
        var pageId = Id;
        _isSaving = true;
        _operationFailed = false;
        try
        {
            var userId = await GetCurrentUserIdAsync();
            if (userId is null)
            {
                SetError("Не удалось определить текущего пользователя.");
                return;
            }
            if (Id != pageId)
                return;
            // Each delegate captures an immutable command (or transfer id), never editable fields.
            _pendingOperation = new(pageId, label, new CommandContext(Guid.NewGuid(), userId), execute, onSuccess);
        }
        catch
        {
            SetError("Не удалось определить текущего пользователя.");
        }
        finally
        {
            _isSaving = false;
        }
        if (_pendingOperation is not null)
            await RetryAsync();
    }

    private async Task RetryAsync()
    {
        if (_isSaving || _pendingOperation is not { } pending)
            return;
        _isSaving = true;
        _operationFailed = false;
        try
        {
            if (await GetCurrentUserIdAsync() != pending.Context.UserId)
            {
                SetError("Повторите операцию под пользователем, который её начал.");
                return;
            }
            var result = await pending.Execute(pending.Context);
            if (_pendingOperation != pending)
                return;
            if (result.IsSuccess || result.Error?.Type != OperationErrorType.Failure)
                _pendingOperation = null;
            if (!result.IsSuccess)
            {
                SetError(result.Error?.Message ?? "Не удалось выполнить операцию перемещения.");
                return;
            }
            await pending.OnSuccess(result.Value);
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

    private sealed record PendingTransferOperation(
        Guid? PageId,
        string Label,
        CommandContext Context,
        Func<CommandContext, Task<OperationResult<Guid>>> Execute,
        Func<Guid, Task> OnSuccess);

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

    private static string FormatQuantity(decimal value) => value.ToString("N3");

    private string GetUserName(string? userId) => string.IsNullOrWhiteSpace(userId)
        ? "—"
        : _userNames.TryGetValue(userId, out var userName)
            ? userName
            : "Пользователь не найден";

    private double KnownPlacedWeightKg => _movements
        .Where(IsPlacedInStorage)
        .Sum(x => x.WeightKg ?? 0);

    private bool IsPlacedWeightComplete => _movements
        .Where(IsPlacedInStorage)
        .All(x => x.Quantity == 0 || x.WeightKg.HasValue);

    private static bool IsPlacedInStorage(InventoryMovement movement) =>
        movement.DestinationStorageLocation?.Zone?.Type == ZoneType.Storage;

    private double? GetPickWeightKg() => WeightCalculation.CalculateKg(
        _pickQuantity,
        _pickSourceBalances.FirstOrDefault(x => x.StockKeepingUnit.Id == _pickSkuId)?.StockKeepingUnit);

    private double? GetPutWeightKg() => WeightCalculation.CalculateKg(
        _putQuantity,
        _transitBalances.FirstOrDefault(x => x.StockKeepingUnit.Id == _putSkuId)?.StockKeepingUnit);

    private double? GetDirectWeightKg() => WeightCalculation.CalculateKg(
        _directQuantity,
        _directSourceBalances.FirstOrDefault(x => x.StockKeepingUnit.Id == _directSkuId)?.StockKeepingUnit);
}
