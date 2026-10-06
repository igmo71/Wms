using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Wms.Application.Commands;
using Wms.Application.ReceivingOrders;
using Wms.Application.Users;
using Wms.Common;
using Wms.Data;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.WebApp.Components.Pages.ReceivingOrderPages;

public partial class ReceivingInProgressView
{
    [Parameter]
    public Guid Id { get; set; }

    [Parameter]
    public EventCallback OnCompleted { get; set; }

    [Inject]
    private ReceivingOrderQueryService OrderQueryService { get; set; } = null!;

    [Inject]
    private ApplicationUserQueryService ApplicationUserQueryService { get; set; } = null!;

    [Inject]
    private ReceivingOrderCommandService OrderCommandService { get; set; } = null!;
    [Inject]
    private ReceivingOrderSynchronizationService SynchronizationService { get; set; } = null!;
    [Inject]
    private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = null!;

    private PendingReceivingCommand<CompleteReceivingCommand>? _pendingCompletion;
    private bool CompletionLocked => _isCompleting || CompletionBlockReason is not null;
    private string? CompletionBlockReason
    {
        get
        {
            if (_order is not { Status: ReceivingOrderStatus.InReceiving })
                return "Ордер не находится в работе.";
            if (_receivingLocation is null)
                return "Не задано место приёмки.";

            int openPalletCount = _order.LicensePlateNumbers.Count(x => x.Status == LicensePlateNumberStatus.Open);
            if (openPalletCount > 0)
                return $"Закройте открытые палеты: {openPalletCount}.";
            if (_order.UnconfirmedItemCount > 0)
                return $"Не проверено строк: {_order.UnconfirmedItemCount}. Укажите факт, включая ноль.";
            if (_completionReconciliation is null)
                return "Сверка с 1С недоступна. Обновите страницу и повторите проверку.";
            if (_completionReconciliation.IsBlocking)
                return "Сверка с 1С блокирует завершение. Проверьте расхождения выше.";
            if (_completionReconciliation.RequiresManager && !_canApproveQuantityDifference)
                return "Количественное расхождение может подтвердить только Manager.";
            if (!_isParticipant
                && !(_completionReconciliation.RequiresManager && _canApproveQuantityDifference))
                return "Завершить приёмку может участник ордера.";
            if (_completionReconciliation.RequiresManager && string.IsNullOrWhiteSpace(_managerReason))
                return "Укажите причину подтверждения количественного расхождения.";

            return null;
        }
    }
    private ReceivingOrder? _order;
    private StorageLocation? _receivingLocation;
    private bool _isLoading = true;
    private bool _isCompleting;
    private bool _completeFailed;
    private string? _errorMessage;
    private string? _synchronizationErrorMessage;
    private OrderSynchronizationAssessment? _synchronizationAssessment;
    private ReceivingCompletionReconciliation? _completionReconciliation;
    private bool _canApproveQuantityDifference;
    private bool _isParticipant;
    private string? _managerReason;
    private IReadOnlyDictionary<string, string> _userNames = new Dictionary<string, string>();

    protected override async Task OnParametersSetAsync()
    {
        if (_pendingCompletion is { } pending && pending.Command.OrderId != Id)
            _pendingCompletion = null;
        _isLoading = true;
        AuthenticationState authenticationState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
        _canApproveQuantityDifference = authenticationState.User.IsInRole(ApplicationRoles.Manager);
        OperationResult<ReceivingCompletionReconciliation> synchronizationResult =
            await SynchronizationService.CheckCompletionAsync(Id);
        _completionReconciliation = synchronizationResult.Value;
        _synchronizationAssessment = synchronizationResult.Value?.Assessment;
        _synchronizationErrorMessage = synchronizationResult.IsSuccess
            ? null
            : synchronizationResult.Error?.Message
                ?? "Не удалось сверить приходный ордер с 1С.";
        _order = await OrderQueryService.GetOrderAsync(Id);
        string? currentUserId = authenticationState.User.FindFirstValue(ClaimTypes.NameIdentifier);
        _isParticipant = currentUserId is not null
            && _order?.Participants.Any(x => x.UserId == currentUserId) == true;
        _userNames = _order is null
            ? new Dictionary<string, string>()
            : await ApplicationUserQueryService.GetUserNamesAsync([
                _order.StartedBy,
                _order.CompletedBy,
                _order.SynchronizationAcknowledgedBy,
                .. _order.Participants.Select(x => x.UserId),
                .. _order.LicensePlateNumbers.SelectMany(x =>
                    new[] { x.ResponsibleUserId, x.CreatedBy, x.ClosedBy, x.ReopenedBy, x.CancelledBy }
                        .Concat(x.Items.SelectMany(item =>
                            new[] { item.CreatedBy, item.UpdatedBy, item.DeletedBy }))
                        .Concat(x.Putaways.Select(putaway => putaway.CreatedBy))) ]);
        _receivingLocation = _order?.ReceivingLocation;
        _isLoading = false;
    }

    private async Task SetReceivedAsync()
    {
        if ((_pendingCompletion is null && CompletionLocked) || _isCompleting)
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
            if (_completionReconciliation is null)
                return;
            _pendingCompletion ??= new(new CompleteReceivingCommand(
                    orderId,
                    locationId,
                    _completionReconciliation.Assessment.Fingerprint,
                    _completionReconciliation.RequiresManager ? _managerReason?.Trim() : null),
                new CommandContext(Guid.NewGuid(), userId));
            var pending = _pendingCompletion;
            var result = await OrderCommandService.CompleteReceivingAsync(
                pending.Command,
                pending.Context,
                _canApproveQuantityDifference);
            if (_pendingCompletion != pending)
                return;
            if (result.IsSuccess || result.Error?.Type != OperationErrorType.Failure)
                _pendingCompletion = null;
            if (result.IsSuccess)
                await OnCompleted.InvokeAsync();
            else
            {
                if (result.Error?.Type == OperationErrorType.Conflict)
                {
                    OperationResult<ReceivingCompletionReconciliation> latest =
                        await SynchronizationService.CheckCompletionAsync(Id);
                    if (latest.IsSuccess)
                    {
                        _completionReconciliation = latest.Value;
                        _synchronizationAssessment = latest.Value!.Assessment;
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

    private decimal? CurrentPlanQuantity(ReceivingOrderItem item) =>
        _completionReconciliation?.CurrentPlan
            .SingleOrDefault(x => x.LineNumber == item.LineNumber
                && x.StockKeepingUnitId == item.StockKeepingUnitId)
            ?.Quantity;

    private async Task<string?> GetCurrentUserIdAsync()
    {
        var authenticationState = await AuthenticationStateProvider.GetAuthenticationStateAsync();

        return authenticationState.User.FindFirstValue(ClaimTypes.NameIdentifier);
    }
}
