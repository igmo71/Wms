using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Wms.Application.ReceivingOrders;
using Wms.Application.StorageLocations;
using Wms.Application.Users;
using Wms.Application.Zones;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;
using Wms.Integration.OneS.Services;

namespace Wms.WebApp.Components.Pages.ReceivingOrderPages;

public partial class InProcess
{
    [Parameter]
    public Guid Id { get; set; }

    [Inject]
    private ReceivingOrderQueryService OrderQueryService { get; set; } = null!;

    [Inject]
    private ApplicationUserQueryService ApplicationUserQueryService { get; set; } = null!;

    [Inject]
    private ReceivingOrderCommandService OrderCommandService { get; set; } = null!;
    [Inject]
    private ReceivingOrderSynchronizationService SynchronizationService { get; set; } = null!;
    [Inject]
    private StorageLocationQueryService StorageLocationQueryService { get; set; } = null!;
    [Inject]
    private ZoneQueryService ZoneQueryService { get; set; } = null!;

    [Inject]
    private NavigationManager NavigationManager { get; set; } = null!;
    [Inject]
    private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = null!;

    private ReceivingOrder? _order;
    private Zone? _receivingZone;
    private StorageLocation? _receivingLocation;
    private bool _isLoading = true;
    private bool _isCompleting;
    private bool _isAcknowledgingSynchronization;
    private bool _completeFailed;
    private string? _errorMessage;
    private string? _synchronizationErrorMessage;
    private OrderSynchronizationAssessment? _synchronizationAssessment;
    private IReadOnlyDictionary<string, string> _userNames = new Dictionary<string, string>();

    protected override async Task OnParametersSetAsync()
    {
        _isLoading = true;
        OperationResult<OrderSynchronizationAssessment> synchronizationResult =
            await SynchronizationService.CheckAsync(Id);
        _synchronizationAssessment = synchronizationResult.Value;
        _synchronizationErrorMessage = synchronizationResult.IsSuccess
            ? null
            : synchronizationResult.Error?.Message
                ?? "Не удалось сверить приходный ордер с 1С.";
        _order = await OrderQueryService.GetOrderAsync(Id);
        _userNames = _order is null
            ? new Dictionary<string, string>()
            : await ApplicationUserQueryService.GetUserNamesAsync([
                _order.StartedBy,
                _order.CompletedBy,
                _order.PutawayStartedBy,
                _order.PutawayCompletedBy,
                _order.ExternalSynchronizationAcknowledgedBy]);
        _receivingZone = _order?.ReceivingLocation?.Zone;
        _receivingLocation = _order?.ReceivingLocation;
        _isLoading = false;
    }

    private async Task AcknowledgeSynchronizationAsync()
    {
        if (_synchronizationAssessment is not { Level: OrderSynchronizationLevel.RequiresOperatorDecision } assessment)
            return;

        string? userId = await GetCurrentUserIdAsync();
        if (userId is null)
        {
            _synchronizationErrorMessage = "Не удалось определить текущего пользователя.";
            return;
        }

        _isAcknowledgingSynchronization = true;
        OperationResult result = await SynchronizationService.AcknowledgeAsync(
            Id, assessment.Fingerprint, userId);
        _isAcknowledgingSynchronization = false;
        if (!result.IsSuccess)
        {
            _synchronizationErrorMessage = result.Error?.Message
                ?? "Не удалось подтвердить расхождения.";
            return;
        }

        _synchronizationErrorMessage = null;
        _synchronizationAssessment = new OrderSynchronizationAssessment(assessment.Fingerprint, []);
        _order = await OrderQueryService.GetOrderAsync(Id);
    }

    private string GetUserName(string? userId) => string.IsNullOrWhiteSpace(userId)
        ? "—"
        : _userNames.TryGetValue(userId, out var userName)
            ? userName
            : "Пользователь не найден";

    private async Task<IEnumerable<Zone>> SearchReceivingZonesAsync(string? searchText, CancellationToken ct)
    {
        if (_order is null)
            return [];

        var result = await ZoneQueryService.ListAsync(new ZoneListQuery
        {
            SearchString = searchText,
            WarehouseId = _order.WarehouseId,
            Type = ZoneType.Receiving,
            SortBy = "Name",
            Take = 10
        }, ct);

        return result.Items;
    }

    private async Task<IEnumerable<StorageLocation>> SearchReceivingLocationsAsync(string? searchText, CancellationToken ct)
    {
        if (_order is null || _receivingZone is null)
            return [];

        var result = await StorageLocationQueryService.ListAsync(new StorageLocationListQuery
        {
            SearchString = searchText,
            WarehouseId = _order.WarehouseId,
            ZoneId = _receivingZone.Id,
            ZoneType = ZoneType.Receiving,
            ExcludeLocked = true,
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

    private async Task UpdateFactQuantityAsync(ReceivingOrderItem item, decimal? factQuantity)
    {
        if (factQuantity is null)
        {
            _completeFailed = true;
            _errorMessage = "Укажите фактическое количество, включая явный ноль.";
            return;
        }

        await UpdateOrderItemFactAsync(item, factQuantity.Value, item.Comment);
    }

    private async Task UpdateCommentAsync(ReceivingOrderItem item, string? comment)
    {
        _completeFailed = false;

        try
        {
            var updateResult = await OrderCommandService
                .UpdateOrderItemCommentAsync(item.ReceivingOrderId, item.LineNumber, comment);

            if (!updateResult.IsSuccess)
            {
                _completeFailed = true;
                _errorMessage = updateResult.Error?.Message ?? "Не удалось обновить комментарий строки.";
                return;
            }

            var localUpdateResult = _order!.UpdateItemComment(item.LineNumber, comment);
            if (!localUpdateResult.IsSuccess)
            {
                _completeFailed = true;
                _errorMessage = localUpdateResult.Error?.Message ?? "Не удалось обновить строку на странице.";
            }
        }
        catch
        {
            _completeFailed = true;
            _errorMessage = "Не удалось обновить комментарий строки.";
        }
    }

    private async Task UpdateOrderItemFactAsync(ReceivingOrderItem item, decimal factQuantity, string? comment)
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

            var localUpdateResult = _order!.UpdateItemFact(item.LineNumber, factQuantity, comment);
            if (!localUpdateResult.IsSuccess)
            {
                _completeFailed = true;
                _errorMessage = localUpdateResult.Error?.Message ?? "Не удалось обновить строку на странице.";
            }
        }
        catch
        {
            _completeFailed = true;
        }
    }

    private static string FormatQuantity(decimal? quantity) =>
        quantity?.ToString("0.###") ?? "—";

    private async Task SetReceivedAsync()
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


            var result = await OrderCommandService.SetReceivedAsync(Id, userId);
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

    private async Task<string?> GetCurrentUserIdAsync()
    {
        var authenticationState = await AuthenticationStateProvider.GetAuthenticationStateAsync();

        return authenticationState.User.FindFirstValue(ClaimTypes.NameIdentifier);
    }
}
