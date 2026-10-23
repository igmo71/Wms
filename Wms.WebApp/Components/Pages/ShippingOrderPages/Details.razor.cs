using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;
using System.Security.Claims;
using Wms.Application.Commands;
using Wms.Application.ShippingOrders;
using Wms.Application.StorageLocations;
using Wms.Application.Users;
using Wms.Application.Zones;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.WebApp.Components.Pages.ShippingOrderPages;

public partial class Details
{
    [Parameter] public Guid Id { get; set; }

    [Inject] private ShippingOrderQueryService OrderQueryService { get; set; } = null!;
    [Inject] private ShippingOrderSynchronizationService SynchronizationService { get; set; } = null!;
    [Inject] private ApplicationUserQueryService ApplicationUserQueryService { get; set; } = null!;
    [Inject] private ShippingOrderCommandService OrderCommandService { get; set; } = null!;
    [Inject] private StorageLocationQueryService StorageLocationQueryService { get; set; } = null!;
    [Inject] private ZoneQueryService ZoneQueryService { get; set; } = null!;
    [Inject] private IDialogService DialogService { get; set; } = null!;
    [Inject] private NavigationManager NavigationManager { get; set; } = null!;
    [Inject] private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = null!;

    private PendingShippingCommand<StartPickingCommand>? _pendingStart;
    private PendingShippingCommand<Guid>? _pendingShipment;
    private bool HasPendingTransition => _isStarting || _isShipping
        || _pendingStart is not null || _pendingShipment is not null;
    private PendingShippingCommand<RollbackShippingOrderCommand>? _pendingRollback;
    private bool _isChoosingRollback;
    private bool RollbackPending => _isRollingBack || _isChoosingRollback || _pendingRollback is not null;
    private bool InputsLocked => HasPendingTransition || RollbackPending || _isAcknowledgingSynchronization;
    private ShippingOrder? _order;
    private IReadOnlyList<ShippingOrderItem> SortedItems => _order?.Items
        .OrderBy(x => x.StockKeepingUnit?.Name ?? x.StockKeepingUnitId.ToString(),
            StringComparer.CurrentCultureIgnoreCase)
        .ThenBy(x => x.LineNumber).ToArray() ?? [];
    private Zone? _shippingZone;
    private StorageLocation? _shippingLocation;
    private bool _isLoading = true;
    private bool _isStarting;
    private bool _isShipping;
    private bool _isRollingBack;
    private bool _isAcknowledgingSynchronization;
    private bool _startOrderFailed;
    private string? _errorMessage;
    private string? _synchronizationErrorMessage;
    private OrderSynchronizationAssessment? _synchronizationAssessment;
    private IReadOnlyDictionary<string, string> _userNames = new Dictionary<string, string>();

    private bool CanRollback => _order?.Status is ShippingOrderStatus.ReadyForPicking
        or ShippingOrderStatus.ReadyForVerification
        or ShippingOrderStatus.InVerification
        or ShippingOrderStatus.Verified
        or ShippingOrderStatus.ReadyForShipment;

    protected override async Task OnParametersSetAsync()
    {
        if (_pendingStart is { } start && start.Input.OrderId != Id)
            _pendingStart = null;
        if (_pendingShipment is { } shipment && shipment.Input != Id)
            _pendingShipment = null;
        if (_pendingRollback is { } rollback && rollback.Input.OrderId != Id)
            _pendingRollback = null;
        await ReloadAsync(checkSynchronization: true);
    }

    private async Task ReloadAsync(bool checkSynchronization = false)
    {
        _isLoading = true;
        if (checkSynchronization)
        {
            OperationResult<OrderSynchronizationAssessment> synchronizationResult =
                await SynchronizationService.CheckAsync(Id);
            _synchronizationAssessment = synchronizationResult.Value;
            _synchronizationErrorMessage = synchronizationResult.IsSuccess
                ? null
                : synchronizationResult.Error?.Message
                    ?? "Не удалось сверить расходный ордер с 1С.";
        }

        _order = await OrderQueryService.GetOrderAsync(Id);
        _shippingZone = _order?.ShippingLocation?.Zone;
        _shippingLocation = _order?.ShippingLocation;
        _userNames = _order is null
            ? new Dictionary<string, string>()
            : await ApplicationUserQueryService.GetUserNamesAsync([
                _order.PickingStartedBy,
                _order.ReadyForShipmentBy,
                _order.ShippedBy,
                _order.RolledBackBy,
                _order.SynchronizationAcknowledgedBy]);
        _isLoading = false;
    }

    private async Task AcknowledgeSynchronizationAsync()
    {
        if (InputsLocked || _synchronizationAssessment is not { Level: OrderSynchronizationLevel.RequiresOperatorDecision } assessment)
            return;

        _isAcknowledgingSynchronization = true;
        try
        {
            string? userId = await GetCurrentUserIdAsync();
            if (userId is null)
            {
                _synchronizationErrorMessage = "Не удалось определить текущего пользователя.";
                return;
            }

            OperationResult result = await SynchronizationService.AcknowledgeAsync(
                Id,
                assessment.Fingerprint,
                userId);
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
                    await ReloadAsync();
                }
                return;
            }

            _synchronizationErrorMessage = null;
            _synchronizationAssessment = new OrderSynchronizationAssessment(assessment.Fingerprint, []);
            await ReloadAsync();
        }
        finally { _isAcknowledgingSynchronization = false; }
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

        var result = await ZoneQueryService.ListAsync(new ZoneListQuery
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

        var result = await StorageLocationQueryService.ListAsync(new StorageLocationListQuery
        {
            SearchString = searchText,
            WarehouseId = _order.WarehouseId,
            ZoneId = _shippingZone.Id,
            ZoneType = ZoneType.Shipping,
            ExcludeLocked = true,
            SortBy = "Name",
            Take = 10
        }, ct);

        return result.Items;
    }

    private Task OnShippingZoneChanged(Zone? shippingZone)
    {
        if (InputsLocked) return Task.CompletedTask;
        _shippingZone = shippingZone;
        _shippingLocation = null;
        return Task.CompletedTask;
    }

    private Task OnShippingLocationChanged(StorageLocation? shippingLocation)
    {
        if (InputsLocked) return Task.CompletedTask;
        _shippingLocation = shippingLocation;
        return Task.CompletedTask;
    }

    private async Task SetReadyForPickingAsync()
    {
        if (RollbackPending || _isAcknowledgingSynchronization || _isStarting || _isShipping || _pendingShipment is not null
            || (_pendingStart is null && _shippingLocation is null))
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

            if (_pendingStart is { } previous && previous.Context.UserId != userId)
                _pendingStart = null;
            if (_pendingStart is null && _shippingLocation is null)
                return;
            _pendingStart ??= new(new StartPickingCommand(Id, _shippingLocation!.Id),
                new CommandContext(Guid.NewGuid(), userId));
            var result = await OrderCommandService.StartPickingAsync(_pendingStart.Input, _pendingStart.Context);
            if (result.IsSuccess || result.Error?.Type != OperationErrorType.Failure)
                _pendingStart = null;
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
        if (InputsLocked || !CanRollback) return;
        var orderId = Id;
        _isChoosingRollback = true;
        DialogResult? dialogResult;
        try
        {
            var dialog = await DialogService.ShowAsync<RollbackDialog>("Откатить расходный ордер");
            dialogResult = await dialog.Result;
        }
        finally { _isChoosingRollback = false; }
        if (InputsLocked || Id != orderId || dialogResult is null || dialogResult.Canceled || dialogResult.Data is not string reason)
            return;
        var command = new RollbackShippingOrderCommand(orderId, reason);
        _isRollingBack = true;
        _startOrderFailed = false;
        try
        {
            var userId = await GetCurrentUserIdAsync();
            if (userId is null) { SetRollbackError("Не удалось определить текущего пользователя."); return; }
            if (Id != orderId) return;
            _pendingRollback = new(command, new CommandContext(Guid.NewGuid(), userId));
        }
        catch { SetRollbackError("Не удалось определить текущего пользователя."); }
        finally { _isRollingBack = false; }
        if (_pendingRollback is not null) await RetryRollbackAsync();
    }

    private async Task RetryRollbackAsync()
    {
        if (_isRollingBack || _pendingRollback is not { } pending) return;
        _isRollingBack = true;
        _startOrderFailed = false;
        try
        {
            if (await GetCurrentUserIdAsync() != pending.Context.UserId)
            {
                SetRollbackError("Повторите операцию под пользователем, который её начал.");
                return;
            }
            if (_pendingRollback != pending) return;
            var result = await OrderCommandService.RollbackAsync(pending.Input, pending.Context);
            if (_pendingRollback != pending) return;
            if (result.IsSuccess || result.Error?.Type != OperationErrorType.Failure) _pendingRollback = null;
            if (!result.IsSuccess) { SetRollbackError(result.Error?.Message ?? "Не удалось откатить расходный ордер."); return; }
            await ReloadAsync();
        }
        catch { SetRollbackError("Не удалось выполнить или обновить откат расходного ордера."); }
        finally { _isRollingBack = false; }
    }

    private void SetRollbackError(string message) { _startOrderFailed = true; _errorMessage = message; }

    private async Task SetShippedAsync()
    {
        if (RollbackPending || _isAcknowledgingSynchronization || _isShipping || _isStarting || _pendingStart is not null)
            return;
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

            if (_pendingShipment is { } previous && previous.Context.UserId != userId)
                _pendingShipment = null;
            _pendingShipment ??= new(Id, new CommandContext(Guid.NewGuid(), userId));
            var result = await OrderCommandService.SetShippedAsync(_pendingShipment.Input, _pendingShipment.Context);
            if (result.IsSuccess || result.Error?.Type != OperationErrorType.Failure)
                _pendingShipment = null;
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
