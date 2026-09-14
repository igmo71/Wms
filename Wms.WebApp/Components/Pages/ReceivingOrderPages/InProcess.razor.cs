using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Wms.Application.Commands;
using Wms.Application.ReceivingOrders;
using Wms.Application.StorageLocations;
using Wms.Application.Users;
using Wms.Application.Zones;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;

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

    private PendingReceivingCommand<CompleteReceivingCommand>? _pendingCompletion;
    private PendingItemOperation? _pendingItem;
    private PendingReceivingCommand<CompleteReceivingWithDiscrepanciesCommand>? _pendingManagerCompletion;
    private string? _completionReason;
    private bool _canManageReceiving;
    private bool _isSavingItem;
    private bool InputsLocked => _isSavingItem || _pendingItem is not null || _isCompleting
        || _pendingCompletion is not null || _pendingManagerCompletion is not null || _isAcknowledgingSynchronization;
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
        if (_pendingCompletion is { } pending && pending.Command.OrderId != Id)
            _pendingCompletion = null;
        if (_pendingItem is { } itemPending && itemPending.OrderId != Id)
            _pendingItem = null;
        if (_pendingManagerCompletion is { } managerPending && managerPending.Command.OrderId != Id)
            _pendingManagerCompletion = null;
        var principal = (await AuthenticationStateProvider.GetAuthenticationStateAsync()).User;
        _canManageReceiving = principal.IsInRole(Wms.Data.ApplicationRoles.Manager)
            || principal.IsInRole(Wms.Data.ApplicationRoles.Administrator);
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
                _order.SynchronizationAcknowledgedBy]);
        _receivingZone = _order?.ReceivingLocation?.Zone;
        _receivingLocation = _order?.ReceivingLocation;
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
                Id, assessment.Fingerprint, userId);
            if (!result.IsSuccess)
            {
                _synchronizationErrorMessage = result.Error?.Message
                    ?? "Не удалось подтвердить расхождения.";
                if (result.Error?.Type == OperationErrorType.Conflict)
                {
                    OperationResult<OrderSynchronizationAssessment> latest =
                        await SynchronizationService.CheckAsync(Id);
                    if (latest.IsSuccess)
                    {
                        _synchronizationAssessment = latest.Value;
                        _order = await OrderQueryService.GetOrderAsync(Id);
                    }
                }
                return;
            }

            _synchronizationErrorMessage = null;
            _synchronizationAssessment = new OrderSynchronizationAssessment(assessment.Fingerprint, []);
            _order = await OrderQueryService.GetOrderAsync(Id);
        }
        finally { _isAcknowledgingSynchronization = false; }
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

    private Task UpdateFactQuantityAsync(ReceivingOrderItem item, decimal? factQuantity)
    {
        if (InputsLocked)
            return Task.CompletedTask;
        if (factQuantity is null)
        {
            _completeFailed = true;
            _errorMessage = "Укажите фактическое количество, включая явный ноль.";
            return Task.CompletedTask;
        }
        var command = new SetReceivingFactCommand(item.ReceivingOrderId, item.LineNumber, factQuantity.Value);
        return RunItemAsync(command.OrderId, "Сохранить факт строки",
            context => OrderCommandService.SetItemFactQuantityAsync(command, context));
    }

    private Task UpdateCommentAsync(ReceivingOrderItem item, string? comment)
    {
        if (InputsLocked)
            return Task.CompletedTask;
        var command = new SetReceivingItemCommentCommand(item.ReceivingOrderId, item.LineNumber, comment);
        return RunItemAsync(command.OrderId, "Сохранить комментарий строки",
            context => OrderCommandService.SetItemCommentAsync(command, context));
    }

    private async Task RunItemAsync(Guid orderId, string label,
        Func<CommandContext, Task<OperationResult<Guid>>> execute)
    {
        if (InputsLocked || orderId != Id)
            return;
        _isSavingItem = true;
        _completeFailed = false;
        try
        {
            var userId = await GetCurrentUserIdAsync();
            if (userId is null)
            {
                _completeFailed = true;
                _errorMessage = "Не удалось определить текущего пользователя.";
                return;
            }
            if (Id != orderId)
                return;
            _pendingItem = new(orderId, label, new(Guid.NewGuid(), userId), execute);
        }
        catch
        {
            _completeFailed = true;
            _errorMessage = "Не удалось определить текущего пользователя.";
        }
        finally { _isSavingItem = false; }
        if (_pendingItem is not null)
            await RetryItemAsync();
    }

    private async Task RetryItemAsync()
    {
        if (_isSavingItem || _pendingItem is not { } pending)
            return;
        _isSavingItem = true;
        _completeFailed = false;
        try
        {
            if (await GetCurrentUserIdAsync() != pending.Context.UserId)
            {
                _completeFailed = true;
                _errorMessage = "Повторите операцию под пользователем, который её начал.";
                return;
            }
            if (_pendingItem != pending)
                return;
            var result = await pending.Execute(pending.Context);
            if (_pendingItem != pending)
                return;
            if (result.IsSuccess || result.Error?.Type != OperationErrorType.Failure)
                _pendingItem = null;
            if (!result.IsSuccess)
            {
                _completeFailed = true;
                _errorMessage = result.Error?.Message ?? "Не удалось сохранить строку приёмки.";
                return;
            }
            // Refresh persisted facts/comments, without a new synchronization or local domain mutation.
            var updated = await OrderQueryService.GetOrderAsync(pending.OrderId);
            if (Id == pending.OrderId)
                _order = updated;
        }
        catch
        {
            _completeFailed = true;
            _errorMessage = "Не удалось сохранить или обновить строку приёмки.";
        }
        finally { _isSavingItem = false; }
    }

    private sealed record PendingItemOperation(Guid OrderId, string Label, CommandContext Context,
        Func<CommandContext, Task<OperationResult<Guid>>> Execute);

    private static string FormatQuantity(decimal? quantity) =>
        quantity?.ToString("0.###") ?? "—";

    private async Task SetReceivedAsync()
    {
        if (_pendingManagerCompletion is not null || _isSavingItem || _pendingItem is not null || _isAcknowledgingSynchronization || _isCompleting || (_pendingCompletion is null && _receivingLocation is null))
            return;

        var orderId = Id;
        var locationId = _receivingLocation?.Id;
        _isCompleting = true;
        _completeFailed = false;

        try
        {
            var userId = await GetCurrentUserIdAsync();

            if (userId is null)
            {
                _completeFailed = true;
                _errorMessage = "Не удалось определить текущего пользователя.";
                return;
            }

            if (Id != orderId)
                return;
            if (_pendingCompletion is { } previous && previous.Context.UserId != userId)
            {
                _completeFailed = true;
                _errorMessage = "Повторите операцию под пользователем, который её начал.";
                return;
            }
            _pendingCompletion ??= new(new CompleteReceivingCommand(orderId, locationId),
                new CommandContext(Guid.NewGuid(), userId));
            var pending = _pendingCompletion;
            var result = await OrderCommandService.CompleteReceivingAsync(pending.Command, pending.Context);
            if (_pendingCompletion != pending)
                return;
            if (result.IsSuccess || result.Error?.Type != OperationErrorType.Failure)
                _pendingCompletion = null;
            if (result.IsSuccess)
                NavigationManager.NavigateTo($"receiving-orders/{Id}");
            else
            {
                if (result.Error?.Type == OperationErrorType.Conflict)
                {
                    OperationResult<OrderSynchronizationAssessment> latest =
                        await SynchronizationService.CheckAsync(Id);
                    if (latest.IsSuccess)
                    {
                        _synchronizationAssessment = latest.Value;
                        _synchronizationErrorMessage = null;
                        _order = await OrderQueryService.GetOrderAsync(Id);
                    }
                    else
                    {
                        _synchronizationErrorMessage = latest.Error?.Message
                            ?? "Не удалось сверить приходный ордер с 1С.";
                    }
                }

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

    private async Task CompleteWithDiscrepanciesAsync()
    {
        if (_isCompleting || _isSavingItem || _pendingItem is not null || _pendingCompletion is not null
            || _isAcknowledgingSynchronization || !_canManageReceiving || _order is null)
            return;
        if (_pendingManagerCompletion is null && (string.IsNullOrWhiteSpace(_completionReason)
            || _receivingLocation is null || _order.SynchronizationFingerprint is null))
            return;
        _isCompleting = true;
        _completeFailed = false;
        try
        {
            var userId = await GetCurrentUserIdAsync();
            if (userId is null) return;
            if (_pendingManagerCompletion is { } previous && previous.Context.UserId != userId)
            {
                _completeFailed = true;
                _errorMessage = "Повторите операцию под пользователем, который её начал.";
                return;
            }
            _pendingManagerCompletion ??= new(new(Id, _receivingLocation!.Id,
                _order.OperationalRevision, _order.SynchronizationFingerprint!, _completionReason!),
                new(Guid.NewGuid(), userId));
            var pending = _pendingManagerCompletion;
            var result = await OrderCommandService.CompleteWithDiscrepanciesAsync(pending.Command, pending.Context);
            if (_pendingManagerCompletion != pending) return;
            if (result.IsSuccess || result.Error?.Type != OperationErrorType.Failure)
                _pendingManagerCompletion = null;
            if (result.IsSuccess)
            {
                NavigationManager.NavigateTo($"receiving-orders/{Id}");
                return;
            }
            _completeFailed = true;
            _errorMessage = result.Error?.Message;
            if (result.Error?.Type == OperationErrorType.Conflict)
            {
                _completionReason = null;
                await OnParametersSetAsync();
            }
        }
        catch
        {
            _completeFailed = true;
            _errorMessage = "Результат завершения не подтвержден. Повторите исходную операцию.";
        }
        finally { _isCompleting = false; }
    }

    private async Task<string?> GetCurrentUserIdAsync()
    {
        var authenticationState = await AuthenticationStateProvider.GetAuthenticationStateAsync();

        return authenticationState.User.FindFirstValue(ClaimTypes.NameIdentifier);
    }
}
