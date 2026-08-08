using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;
using Wms.Application.Services;
using Wms.Common;
using Wms.Domain;

namespace Wms.WebApp.Components.Pages.ReceivingOrderPages;

public partial class InProcess
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
    private bool _isCompleting;
    private bool _completeFailed;
    private string? _errorMessage;

    protected override async Task OnParametersSetAsync()
    {
        _isLoading = true;
        _order = await OrderQueryService.GetOrderAsync(Id);
        _receivingZone = _order?.ReceivingLocation?.Zone;
        _receivingLocation = _order?.ReceivingLocation;
        _isLoading = false;
    }

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

    private async Task UpdateFactQuantityAsync(ReceivingOrderItem item, double factQuantity)
    {
        await UpdateOrderItemAsync(item, factQuantity, item.Comment);
    }

    private async Task UpdateCommentAsync(ReceivingOrderItem item, string? comment)
    {
        await UpdateOrderItemAsync(item, item.FactQuantity, comment);
    }

    private async Task UpdateOrderItemAsync(ReceivingOrderItem item, double factQuantity, string? comment)
    {
        _completeFailed = false;

        try
        {
            var updateResult = await OrderCommandService
                .UpdateOrderItemFactQuantityAsync(item.ReceivingOrderId, item.LineNumber, factQuantity, comment);

            if (!updateResult.IsSuccess)
            {
                _completeFailed = true;
                _errorMessage = updateResult.Error?.Message ?? "Не удалось обновить количество по факту.";
                return;
            }

            item.FactQuantity = factQuantity;
            item.Comment = comment;
        }
        catch
        {
            _completeFailed = true;
        }
    }

    private async Task CompleteOrderAsync()
    {
        if (_receivingLocation is not StorageLocation receivingLocation)
            return;

        _isCompleting = true;
        _completeFailed = false;

        var userId = await GetCurrentUserIdAsync();

        if (userId is null)
        {
            _completeFailed = true;
            _errorMessage = "Не удалось определить текущего пользователя.";
            return;
        }

        try
        {

            var setLocationResult = await OrderCommandService.SetReceivingLocationAsync(Id, receivingLocation.Id);
            if (!setLocationResult.IsSuccess)
            {
                _completeFailed = true;
                _errorMessage = setLocationResult.Error?.Message ?? "Не удалось сохранить место приёмки";
                return;
            }


            var result = await OrderCommandService.CompleteOrderAsync(Id, userId);
            if (result.IsSuccess)
                NavigationManager.NavigateTo($"receiving-orders/{Id}");
            else
            {
                _completeFailed = true;
                _errorMessage = result.Error?.Message ?? "Не удалось завершить приходный ордер.";
            }
        }
        catch
        {
            _completeFailed = true;
        }
        finally
        {
            _isCompleting = false;
        }
    }

    private static string FormatDateTimeOffset(DateTimeOffset? value) =>
        value?.ToLocalTime().ToString("dd.MM.yyyy HH:mm") ?? "—";

    private async Task<string?> GetCurrentUserIdAsync()
    {
        var authenticationState = await AuthenticationStateProvider.GetAuthenticationStateAsync();

        return authenticationState.User.FindFirstValue(ClaimTypes.NameIdentifier);
    }
}
