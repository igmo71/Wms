using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;
using System.Security.Claims;
using Wms.Application.Services;
using Wms.Application.Services.ShippingOrders;
using Wms.Application.StorageLocations;
using Wms.Application.Zones;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.WebApp.Components.Pages.ShippingOrderPages;

public partial class Details
{
    [Parameter] public Guid Id { get; set; }

    [Inject] private ShippingOrderQueryService OrderQueryService { get; set; } = null!;
    [Inject] private ApplicationUserQueryService ApplicationUserQueryService { get; set; } = null!;
    [Inject] private ShippingOrderCommandService OrderCommandService { get; set; } = null!;
    [Inject] private StorageLocationService StorageLocationService { get; set; } = null!;
    [Inject] private ZoneService ZoneService { get; set; } = null!;
    [Inject] private IDialogService DialogService { get; set; } = null!;
    [Inject] private NavigationManager NavigationManager { get; set; } = null!;
    [Inject] private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = null!;

    private ShippingOrder? _order;
    private Zone? _shippingZone;
    private StorageLocation? _shippingLocation;
    private bool _isLoading = true;
    private bool _isStarting;
    private bool _isShipping;
    private bool _isRollingBack;
    private bool _startOrderFailed;
    private string? _errorMessage;
    private IReadOnlyDictionary<string, string> _userNames = new Dictionary<string, string>();

    private bool CanRollback => _order?.Status is ShippingOrderStatus.ReadyForPicking
        or ShippingOrderStatus.ReadyForVerification
        or ShippingOrderStatus.InVerification
        or ShippingOrderStatus.Verified
        or ShippingOrderStatus.ReadyForShipment;

    protected override async Task OnParametersSetAsync()
    {
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        _isLoading = true;
        _order = await OrderQueryService.GetOrderAsync(Id);
        _shippingZone = _order?.ShippingLocation?.Zone;
        _shippingLocation = _order?.ShippingLocation;
        _userNames = _order is null
            ? new Dictionary<string, string>()
            : await ApplicationUserQueryService.GetUserNamesAsync([
                _order.PickingStartedBy,
                _order.ReadyForShipmentBy,
                _order.ShippedBy,
                _order.RolledBackBy]);
        _isLoading = false;
    }

    private static string FormatDateTime(DateTime? value) =>
        value?.ToLocalTime().ToString("dd.MM.yyyy HH:mm") ?? "—";

    private string GetUserName(string? userId) => string.IsNullOrWhiteSpace(userId)
        ? "—"
        : _userNames.TryGetValue(userId, out var userName)
            ? userName
            : "Пользователь не найден";

    private async Task<IEnumerable<Zone>> SearchShippingZonesAsync(string? searchText, CancellationToken ct)
    {
        if (_order is null)
            return [];

        var result = await ZoneService.ListAsync(new ZoneListQuery
        {
            SearchString = searchText,
            WarehouseId = _order.WarehouseId,
            Type = ZoneType.Shipping,
            SortBy = "Name",
            Take = 10
        }, ct);

        return result.Items;
    }

    private async Task<IEnumerable<StorageLocation>> SearchShippingLocationsAsync(string? searchText, CancellationToken ct)
    {
        if (_order is null || _shippingZone is null)
            return [];

        var result = await StorageLocationService.ListAsync(new StorageLocationListQuery
        {
            SearchString = searchText,
            WarehouseId = _order.WarehouseId,
            ZoneId = _shippingZone.Id,
            ZoneType = ZoneType.Shipping,
            SortBy = "Name",
            Take = 10
        }, ct);

        return result.Items;
    }

    private Task OnShippingZoneChanged(Zone? shippingZone)
    {
        _shippingZone = shippingZone;
        _shippingLocation = null;
        return Task.CompletedTask;
    }

    private Task OnShippingLocationChanged(StorageLocation? shippingLocation)
    {
        _shippingLocation = shippingLocation;
        return Task.CompletedTask;
    }

    private async Task SetReadyForPickingAsync()
    {
        if (_shippingLocation is not StorageLocation shippingLocation)
            return;

        _isStarting = true;
        _startOrderFailed = false;

        try
        {
            var userId = await GetCurrentUserIdAsync();
            if (userId is null)
            {
                _startOrderFailed = true;
                _errorMessage = "Не удалось определить текущего пользователя.";
                return;
            }

            var setLocationResult = await OrderCommandService.SetShippingLocationAsync(Id, shippingLocation.Id);
            if (!setLocationResult.IsSuccess)
            {
                _startOrderFailed = true;
                _errorMessage = setLocationResult.Error?.Message ?? "Не удалось сохранить место отгрузки.";
                return;
            }

            var result = await OrderCommandService.SetReadyForPickingAsync(Id, userId);
            if (!result.IsSuccess)
            {
                _startOrderFailed = true;
                _errorMessage = result.Error?.Message ?? "Не удалось взять ордер в отбор.";
                return;
            }

            NavigationManager.NavigateTo($"/shipping-orders/{Id}/picking");
        }
        catch
        {
            _startOrderFailed = true;
            _errorMessage = "Не удалось взять ордер в отбор.";
        }
        finally
        {
            _isStarting = false;
        }
    }

    private async Task ShowRollbackDialogAsync()
    {
        var dialog = await DialogService.ShowAsync<RollbackDialog>("Откатить расходный ордер");
        var dialogResult = await dialog.Result;

        if (dialogResult is null || dialogResult.Canceled || dialogResult.Data is not string reason)
            return;

        _isRollingBack = true;
        _startOrderFailed = false;

        try
        {
            var userId = await GetCurrentUserIdAsync();
            if (userId is null)
            {
                _startOrderFailed = true;
                _errorMessage = "Не удалось определить текущего пользователя.";
                return;
            }

            var result = await OrderCommandService.RollbackAsync(Id, reason, userId);
            if (!result.IsSuccess)
            {
                _startOrderFailed = true;
                _errorMessage = result.Error?.Message ?? "Не удалось откатить расходный ордер.";
                return;
            }

            await ReloadAsync();
        }
        catch
        {
            _startOrderFailed = true;
            _errorMessage = "Не удалось откатить расходный ордер.";
        }
        finally
        {
            _isRollingBack = false;
        }
    }

    private async Task SetShippedAsync()
    {
        _isShipping = true;
        _startOrderFailed = false;

        try
        {
            var userId = await GetCurrentUserIdAsync();
            if (userId is null)
            {
                _startOrderFailed = true;
                _errorMessage = "Не удалось определить текущего пользователя.";
                return;
            }

            var result = await OrderCommandService.SetShippedAsync(Id, userId);
            if (!result.IsSuccess)
            {
                _startOrderFailed = true;
                _errorMessage = result.Error?.Message ?? "Не удалось отгрузить ордер.";
                return;
            }

            await ReloadAsync();
        }
        catch
        {
            _startOrderFailed = true;
            _errorMessage = "Не удалось отгрузить ордер.";
        }
        finally
        {
            _isShipping = false;
        }
    }

    private async Task<string?> GetCurrentUserIdAsync()
    {
        var authenticationState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
        return authenticationState.User.FindFirstValue(ClaimTypes.NameIdentifier);
    }
}
