using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;
using Wms.Application.Services;
using Wms.Common;
using Wms.Domain;

namespace Wms.WebApp.Components.Pages.ShippingOrderPages;

public partial class Details
{
    [Parameter] public Guid Id { get; set; }

    [Inject] private ShippingOrderQueryService OrderQueryService { get; set; } = null!;
    [Inject] private ShippingOrderCommandService OrderCommandService { get; set; } = null!;
    [Inject] private StorageLocationService StorageLocationService { get; set; } = null!;
    [Inject] private ZoneService ZoneService { get; set; } = null!;
    [Inject] private NavigationManager NavigationManager { get; set; } = null!;
    [Inject] private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = null!;

    private ShippingOrder? _order;
    private Zone? _shippingZone;
    private StorageLocation? _shippingLocation;
    private bool _isLoading = true;
    private bool _isStarting;
    private bool _startOrderFailed;
    private string? _errorMessage;

    protected override async Task OnParametersSetAsync()
    {
        _isLoading = true;
        _order = await OrderQueryService.GetOrderAsync(Id);
        _shippingZone = _order?.ShippingLocation?.Zone;
        _shippingLocation = _order?.ShippingLocation;
        _isLoading = false;
    }

    private static string FormatDateTime(DateTime? value) =>
        value?.ToLocalTime().ToString("dd.MM.yyyy HH:mm") ?? "—";

    private async Task<IEnumerable<Zone>> SearchShippingZonesAsync(string? searchText, CancellationToken ct)
    {
        if (_order is null)
            return [];

        var result = await ZoneService.ListAsync(new ZoneListQuery
        {
            SearchString = searchText,
            WarehouseId = _order.WarehouseId,
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

    private async Task<string?> GetCurrentUserIdAsync()
    {
        var authenticationState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
        return authenticationState.User.FindFirstValue(ClaimTypes.NameIdentifier);
    }
}
