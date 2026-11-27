using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;
using System.Security.Claims;
using Wms.Application.Inventory.Counts;
using Wms.Application.Commands;
using Wms.Application.Users;
using Wms.Application.Warehouses;
using Wms.Common;
using Wms.Data;
using Wms.Domain;

namespace Wms.WebApp.Components.Pages.InventoryCountPages;

public partial class Index
{
    [Inject] private InventoryCountQueryService InventoryCountQueryService { get; set; } = null!;
    [Inject] private StockAvailabilityStore StockAvailability { get; set; } = null!;
    [Inject] private ApplicationUserQueryService ApplicationUserQueryService { get; set; } = null!;
    [Inject] private InventoryCountCommandService InventoryCountCommandService { get; set; } = null!;
    [Inject] private WarehouseService WarehouseService { get; set; } = null!;
    [Inject] private NavigationManager NavigationManager { get; set; } = null!;
    [Inject] private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = null!;
    [Inject] private ILogger<Index> Logger { get; set; } = null!;

    private MudDataGrid<InventoryCount> _dataGrid = null!;
    private Warehouse? _warehouse;
    private StorageLocation? _storageLocation;
    private bool _isCreating;
    private (StartInventoryCountCommand Command, CommandContext Context)? _pendingStart;
    private (Guid LocationId, bool Allowed, CommandContext Context)? _pendingMixing;
    private bool InputsLocked => _isCreating || _pendingStart is not null || _pendingMixing is not null;
    private bool _createFailed;
    private string? _errorMessage;
    private IReadOnlyDictionary<string, string> _userNames = new Dictionary<string, string>();

    private async Task<GridData<InventoryCount>> LoadServerDataAsync(
        GridState<InventoryCount> state,
        CancellationToken cancellationToken)
    {
        var sortDefinition = state.SortDefinitions.FirstOrDefault();
        var result = await InventoryCountQueryService.ListAsync(new ListQuery
        {
            SortBy = sortDefinition?.SortBy,
            SortDescending = sortDefinition?.Descending ?? true,
            Skip = state.Page * state.PageSize,
            Take = state.PageSize
        }, cancellationToken);

        _userNames = await ApplicationUserQueryService.GetUserNamesAsync(
            result.Items.Select(x => x.PostedBy), cancellationToken);

        return new GridData<InventoryCount>
        {
            Items = result.Items,
            TotalItems = result.TotalItems
        };
    }

    private string GetUserName(string? userId) => string.IsNullOrWhiteSpace(userId)
        ? "—"
        : _userNames.TryGetValue(userId, out var userName)
            ? userName
            : "Пользователь не найден";

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

    private Task OnWarehouseChanged(Warehouse? warehouse)
    {
        _warehouse = warehouse;
        _storageLocation = null;
        return Task.CompletedTask;
    }

    private async Task<IEnumerable<StorageLocation>> SearchStorageLocationsAsync(
        string? searchText,
        CancellationToken ct)
    {
        if (_warehouse is null)
            return [];

        return await StockAvailability.SearchStorageCellsAsync(_warehouse.Id, searchText, ct);
    }

    private static string GetLocationText(StorageLocation? location) =>
        location is null ? string.Empty : $"{StorageLocationDisplay.Format(location)} · {location.Zone?.Name}";

    private async Task ChangeMixingAsync()
    {
        if (_isCreating || _pendingStart is not null || _storageLocation is not { } location)
            return;
        _isCreating = true;
        _createFailed = false;
        try
        {
            var userId = await GetCurrentUserIdAsync();
            if (userId is null)
            {
                SetError("Не удалось определить текущего пользователя.");
                return;
            }
            _pendingMixing ??= (location.Id, !location.AllowsMultipleSkus, new(Guid.NewGuid(), userId));
            var pending = _pendingMixing.Value;
            if (pending.Context.UserId != userId)
            {
                SetError("Повторите операцию под пользователем, который её начал.");
                return;
            }
            var result = await InventoryCountCommandService.SetSkuMixingAsync(
                pending.LocationId, pending.Allowed, pending.Context);
            if (result.IsSuccess || result.Error?.Type != OperationErrorType.Failure)
                _pendingMixing = null;
            if (result.IsSuccess)
                location.SetSkuMixing(pending.Allowed);
            else
                SetError(result.Error?.Message ?? "Не удалось изменить разрешение смешения.");
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Failed to change SKU mixing for {StorageLocationId}.", location.Id);
            SetError("Не удалось изменить разрешение смешения.");
        }
        finally { _isCreating = false; }
    }

    private async Task CreateAsync()
    {
        if (InputsLocked || _warehouse is not Warehouse warehouse || _storageLocation is not StorageLocation location)
            return;
        var command = new StartInventoryCountCommand(warehouse.Id, location.Id);
        _isCreating = true;
        _createFailed = false;
        try
        {
            var userId = await GetCurrentUserIdAsync();
            if (userId is null)
            {
                SetError("Не удалось определить текущего пользователя.");
                return;
            }
            _pendingStart = (command, new(Guid.NewGuid(), userId));
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Failed to identify inventory count user.");
            SetError("Не удалось определить текущего пользователя.");
        }
        finally { _isCreating = false; }
        if (_pendingStart is not null)
            await RetryAsync();
    }

    private async Task RetryAsync()
    {
        if (_isCreating || _pendingStart is not { } pending)
            return;
        _isCreating = true;
        _createFailed = false;
        try
        {
            if (await GetCurrentUserIdAsync() != pending.Context.UserId)
            {
                SetError("Повторите операцию под пользователем, который её начал.");
                return;
            }
            var result = await InventoryCountCommandService.StartAsync(pending.Command, pending.Context);
            if (result.IsSuccess || result.Error?.Type != OperationErrorType.Failure)
                _pendingStart = null;
            if (!result.IsSuccess)
            {
                SetError(result.Error?.Message ?? "Не удалось создать инвентаризацию.");
                return;
            }
            NavigationManager.NavigateTo($"inventory-counts/{result.Value}");
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Failed to start inventory count for {StorageLocationId}.", pending.Command.StorageLocationId);
            SetError("Не удалось создать инвентаризацию.");
        }
        finally { _isCreating = false; }
    }

    private void SetError(string message) { _createFailed = true; _errorMessage = message; }

    private async Task<string?> GetCurrentUserIdAsync()
    {
        var authenticationState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
        return authenticationState.User.FindFirstValue(ClaimTypes.NameIdentifier);
    }
}
