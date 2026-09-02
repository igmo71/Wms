using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;
using Wms.Application.ReceivingOrders;
using Wms.Application.StorageLocations;
using Wms.Application.Users;
using Wms.Application.Zones;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;
using Wms.Integration.OneS.Services;

namespace Wms.WebApp.Components.Pages.ReceivingOrderPages;

public partial class Details
{
    private static string FormatQuantity(decimal? quantity, string emptyText = "—") =>
        quantity?.ToString("0.###") ?? emptyText;

    [Parameter]
    public Guid Id { get; set; }

    [Inject]
    private ReceivingOrderQueryService OrderQueryService { get; set; } = null!;
    [Inject]
    private ReceivingOrderSynchronizationService SynchronizationService { get; set; } = null!;
    [Inject]
    private ApplicationUserQueryService ApplicationUserQueryService { get; set; } = null!;
    [Inject]
    private ReceivingOrderCommandService OrderCommandService { get; set; } = null!;
    [Inject]
    private PutawayCommandService PutawayCommandService { get; set; } = null!;
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
    private bool _isStarting;
    private bool _isStartingPutaway;
    private bool _isAcknowledgingSynchronization;
    private bool _startOrderFailed;
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
        _receivingZone = _order?.ReceivingLocation?.Zone;
        _receivingLocation = _order?.ReceivingLocation;
        await LoadUserNamesAsync();
        _isLoading = false;
    }

    private async Task LoadUserNamesAsync()
    {
        _userNames = _order is null
            ? new Dictionary<string, string>()
            : await ApplicationUserQueryService.GetUserNamesAsync([
                _order.StartedBy,
                _order.CompletedBy,
                _order.PutawayStartedBy,
                _order.PutawayCompletedBy,
                _order.SynchronizationAcknowledgedBy]);
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
            Id,
            assessment.Fingerprint,
            userId);
        _isAcknowledgingSynchronization = false;
        if (!result.IsSuccess)
        {
            _synchronizationErrorMessage = result.Error?.Message
                ?? "Не удалось подтвердить расхождения.";
            if (result.Error?.Type == OperationErrorType.Conflict)
            {
                OperationResult<OrderSynchronizationAssessment> latestAssessment =
                    await SynchronizationService.CheckAsync(Id);
                if (latestAssessment.IsSuccess)
                    _synchronizationAssessment = latestAssessment.Value;
                _order = await OrderQueryService.GetOrderAsync(Id);
                _receivingZone = _order?.ReceivingLocation?.Zone;
                _receivingLocation = _order?.ReceivingLocation;
                await LoadUserNamesAsync();
            }
            return;
        }

        _synchronizationErrorMessage = null;
        _synchronizationAssessment = new OrderSynchronizationAssessment(assessment.Fingerprint, []);
        _order = await OrderQueryService.GetOrderAsync(Id);
        _receivingZone = _order?.ReceivingLocation?.Zone;
        _receivingLocation = _order?.ReceivingLocation;
        await LoadUserNamesAsync();
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

    private async Task SetInReceivingAsync()
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

            var result = await OrderCommandService.SetInReceivingAsync(Id, userId);
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

    private async Task StartPutawayAsync()
    {
        _isStartingPutaway = true;
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

            var result = await PutawayCommandService.StartAsync(Id, userId);
            if (!result.IsSuccess)
            {
                _startOrderFailed = true;
                _errorMessage = result.Error?.Message ?? "Не удалось начать размещение.";
                return;
            }

            NavigationManager.NavigateTo($"receiving-orders/{Id}/putaway");
        }
        catch
        {
            _startOrderFailed = true;
            _errorMessage = "Не удалось начать размещение.";
        }
        finally
        {
            _isStartingPutaway = false;
        }
    }

    private async Task<string?> GetCurrentUserIdAsync()
    {
        var authenticationState = await AuthenticationStateProvider.GetAuthenticationStateAsync();

        return authenticationState.User.FindFirstValue(ClaimTypes.NameIdentifier);
    }
}
