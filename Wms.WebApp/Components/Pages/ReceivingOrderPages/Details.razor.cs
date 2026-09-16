using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;
using System.Text.Json;
using Wms.Application.Commands;
using Wms.Application.ReceivingOrders;
using Wms.Application.StorageLocations;
using Wms.Application.Users;
using Wms.Application.Zones;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;

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
    private StorageLocationQueryService StorageLocationQueryService { get; set; } = null!;
    [Inject]
    private ZoneQueryService ZoneQueryService { get; set; } = null!;
    [Inject]
    private NavigationManager NavigationManager { get; set; } = null!;
    [Inject]
    private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = null!;

    private PendingReceivingCommand<StartReceivingCommand>? _pendingStart;
    private ReceivingOrder? _order;
    private Zone? _receivingZone;
    private StorageLocation? _receivingLocation;
    private bool _isLoading = true;
    private bool _isStarting;
    private bool _isAcknowledgingSynchronization;
    private bool _startOrderFailed;
    private string? _errorMessage;
    private string? _synchronizationErrorMessage;
    private OrderSynchronizationAssessment? _synchronizationAssessment;
    private IReadOnlyList<ReceivingCompletionPlanLine> _completionCurrentPlan = [];
    private IReadOnlyDictionary<string, string> _userNames = new Dictionary<string, string>();

    protected override async Task OnParametersSetAsync()
    {
        if (_pendingStart is { } pending && pending.Command.OrderId != Id)
            _pendingStart = null;
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
                : synchronizationResult.Error?.Message
                    ?? "Не удалось сверить приходный ордер с 1С.";
        }
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
        if (_order?.Status == ReceivingOrderStatus.Received
            || _synchronizationAssessment is not { Level: OrderSynchronizationLevel.RequiresOperatorDecision } assessment)
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
        finally { _isAcknowledgingSynchronization = false; }
    }

    private string GetUserName(string? userId) => string.IsNullOrWhiteSpace(userId)
        ? "—"
        : _userNames.TryGetValue(userId, out var userName)
            ? userName
            : "Пользователь не найден";

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
        if (_isAcknowledgingSynchronization || _isStarting || (_pendingStart is null && _receivingLocation is null))
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
            _pendingStart ??= new(new StartReceivingCommand(Id, _receivingLocation!.Id),
                new CommandContext(Guid.NewGuid(), userId));
            var result = await OrderCommandService.StartReceivingAsync(
                _pendingStart.Command, _pendingStart.Context);
            if (result.IsSuccess || result.Error?.Type != OperationErrorType.Failure)
                _pendingStart = null;
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
