using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;
using Wms.Application;
using Wms.Application.ReceivingOrders;
using Wms.Common;
using Wms.Domain;

namespace Wms.WebApp.Components.Pages.ReceivingOrderPages;

public partial class Details
{
    [Parameter]
    public Guid Id { get; set; }

    [Inject]
    private ReceivingOrderQueryService OrderQueryService { get; set; } = null!;
    [Inject]
    private ReceivingOrderCommandService OrderCommandService { get; set; } = null!;
    [Inject]
    private StorageLocationService StorageLocationService { get; set; } = null!;
    [Inject]
    private ZoneService ZoneService { get; set; } = null!;
    [Inject]
    private NavigationManager NavigationManager { get; set; } = null!;
    [Inject]
    private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = null!;

    private ReceivingOrder? _order;
    private Zone? _receivingZone;
    private StorageLocation? _receivingLocation;
    private bool _isLoading = true;
    private bool _isStarting;
    private bool _startOrderFailed;
    private string? _errorMessage;

    protected override async Task OnParametersSetAsync()
    {
        _isLoading = true;

        _order = await OrderQueryService.GetOrderAsync(Id);
        _receivingZone = _order?.ReceivingLocation?.Zone;
        _receivingLocation = _order?.ReceivingLocation;
        _isLoading = false;
    }

    private static string FormatDateTimeOffset(DateTimeOffset? value) =>
        value?.ToLocalTime().ToString("dd.MM.yyyy HH:mm") ?? "—";

    private async Task<IEnumerable<Zone>> SearchReceivingZonesAsync(string? searchText, CancellationToken ct)
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

    private async Task<IEnumerable<StorageLocation>> SearchReceivingLocationsAsync(string? searchText, CancellationToken ct)
    {
        if (_order is null || _receivingZone is null)
            return [];

        var result = await StorageLocationService.ListAsync(new StorageLocationListQuery
        {
            SearchString = searchText,
            WarehouseId = _order.WarehouseId,
            ZoneId = _receivingZone.Id,
            SortBy = "Name",
            Take = 10
        }, ct);

        return result.Items;
    }

    private Task OnReceivingZoneChanged(Zone? receivingZone)
    {
        _receivingZone = receivingZone;
        _receivingLocation = null;
        return Task.CompletedTask;
    }

    private Task OnReceivingLocationChanged(StorageLocation? receivingLocation)
    {
        _receivingLocation = receivingLocation;
        return Task.CompletedTask;
    }

    private async Task StartOrderAsync()
    {
        if (_receivingLocation is not StorageLocation receivingLocation)
            return;

        _isStarting = true;
        _startOrderFailed = false;

        var userId = await GetCurrentUserIdAsync();

        if (userId is null)
        {
            _startOrderFailed = true;
            _errorMessage = "Не удалось определить текущего пользователя.";
            return;
        }

        try
        {
            var setLocationResult = await OrderCommandService.SetReceivingLocationAsync(Id, receivingLocation.Id);
            if (!setLocationResult.IsSuccess)
            {
                _startOrderFailed = true;
                _errorMessage = setLocationResult.Error?.Message ?? "Не удалось сохранить место приёмки";
                return;
            }

            var result = await OrderCommandService.StartOrderAsync(Id, userId);
            if (!result.IsSuccess)
            {
                _startOrderFailed = true;
                _errorMessage = result.Error?.Message ?? "Не удалось взять ордер в работу";
                return;
            }
            NavigationManager.NavigateTo($"receiving-orders/{Id}/in-process");

        }
        catch
        {
            _startOrderFailed = true;
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
