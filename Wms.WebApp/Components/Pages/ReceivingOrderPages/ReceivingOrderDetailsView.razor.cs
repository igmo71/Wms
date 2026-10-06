using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Wms.Application.ReceivingOrders;
using Wms.Application.Users;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.WebApp.Components.Pages.ReceivingOrderPages;

public partial class ReceivingOrderDetailsView
{
    [Parameter] public Guid Id { get; set; }

    [Inject] private ReceivingOrderQueryService OrderQueryService { get; set; } = null!;
    [Inject] private ReceivingOrderSynchronizationService SynchronizationService { get; set; } = null!;
    [Inject] private ApplicationUserQueryService ApplicationUserQueryService { get; set; } = null!;
    [Inject] private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = null!;

    private ReceivingOrder? _order;
    private bool _isLoading = true;
    private bool _isAcknowledgingSynchronization;
    private string? _synchronizationErrorMessage;
    private OrderSynchronizationAssessment? _synchronizationAssessment;
    private IReadOnlyList<ReceivingCompletionPlanLine> _completionCurrentPlan = [];
    private IReadOnlyDictionary<string, string> _userNames = new Dictionary<string, string>();

    protected override async Task OnParametersSetAsync()
    {
        _isLoading = true;
        _order = await OrderQueryService.GetOrderAsync(Id);
        if (_order?.Status == ReceivingOrderStatus.Received)
        {
            _completionCurrentPlan = Deserialize<IReadOnlyList<ReceivingCompletionPlanLine>>(
                _order.CompletionCurrentPlanJson) ?? [];
            var differences = Deserialize<IReadOnlyList<OrderSynchronizationDifference>>(
                _order.CompletionDifferencesJson) ?? [];
            _synchronizationAssessment = string.IsNullOrWhiteSpace(_order.CompletionFingerprint)
                ? null
                : new OrderSynchronizationAssessment(_order.CompletionFingerprint, differences);
            _synchronizationErrorMessage = null;
        }
        else
        {
            OperationResult<OrderSynchronizationAssessment> synchronizationResult =
                await SynchronizationService.CheckAsync(Id);
            _synchronizationAssessment = synchronizationResult.Value;
            _synchronizationErrorMessage = synchronizationResult.IsSuccess
                ? null
                : synchronizationResult.Error?.Message ?? "Не удалось сверить приходный ордер с 1С.";
        }

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
                _order.SynchronizationAcknowledgedBy,
                _order.CompletionApprovedBy,
                .. _order.Participants.Select(x => x.UserId),
                .. _order.LicensePlateNumbers.SelectMany(x =>
                    new[] { x.ResponsibleUserId, x.CreatedBy, x.ClosedBy, x.ReopenedBy, x.CancelledBy }
                        .Concat(x.Items.SelectMany(item =>
                            new[] { item.CreatedBy, item.UpdatedBy, item.DeletedBy }))
                        .Concat(x.Putaways.Select(putaway => putaway.CreatedBy))) ]);
    }

    private async Task AcknowledgeSynchronizationAsync()
    {
        if (_order?.Status != ReceivingOrderStatus.ReadyForReceiving
            || _synchronizationAssessment is not { Level: OrderSynchronizationLevel.RequiresOperatorDecision } assessment)
            return;

        _isAcknowledgingSynchronization = true;
        try
        {
            string? userId = (await AuthenticationStateProvider.GetAuthenticationStateAsync())
                .User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null)
            {
                _synchronizationErrorMessage = "Не удалось определить текущего пользователя.";
                return;
            }

            OperationResult result = await SynchronizationService.AcknowledgeAsync(
                Id, assessment.Fingerprint, userId);
            if (!result.IsSuccess)
            {
                _synchronizationErrorMessage = result.Error?.Message ?? "Не удалось подтвердить расхождения.";
                if (result.Error?.Type == OperationErrorType.Conflict)
                {
                    OperationResult<OrderSynchronizationAssessment> latestAssessment =
                        await SynchronizationService.CheckAsync(Id);
                    if (latestAssessment.IsSuccess)
                        _synchronizationAssessment = latestAssessment.Value;
                    _order = await OrderQueryService.GetOrderAsync(Id);
                    await LoadUserNamesAsync();
                }
                return;
            }

            _synchronizationErrorMessage = null;
            _synchronizationAssessment = new OrderSynchronizationAssessment(assessment.Fingerprint, []);
            _order = await OrderQueryService.GetOrderAsync(Id);
            await LoadUserNamesAsync();
        }
        finally { _isAcknowledgingSynchronization = false; }
    }

    private decimal? CompletionCurrentPlanQuantity(ReceivingOrderItem item) =>
        _completionCurrentPlan
            .SingleOrDefault(x => x.LineNumber == item.LineNumber
                && x.StockKeepingUnitId == item.StockKeepingUnitId)
            ?.Quantity;

    private static T? Deserialize<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return default;
        try { return JsonSerializer.Deserialize<T>(json); }
        catch (JsonException) { return default; }
    }

}
