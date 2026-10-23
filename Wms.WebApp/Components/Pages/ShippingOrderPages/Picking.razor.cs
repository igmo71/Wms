using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;
using Wms.Application.Commands;
using Wms.Application.ShippingOrders;
using Wms.Application.Users;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.WebApp.Components.Pages.ShippingOrderPages;

public partial class Picking
{
    [Parameter] public Guid Id { get; set; }

    [Inject] private ShippingOrderQueryService OrderQueryService { get; set; } = null!;
    [Inject] private ApplicationUserQueryService ApplicationUserQueryService { get; set; } = null!;
    [Inject] private ShippingOrderCommandService OrderCommandService { get; set; } = null!;
    [Inject] private ShippingOrderSynchronizationService SynchronizationService { get; set; } = null!;
    [Inject] private PickingQueryService PickingQueryService { get; set; } = null!;
    [Inject] private PickingCommandService PickingCommandService { get; set; } = null!;
    [Inject] private IDialogService DialogService { get; set; } = null!;
    [Inject] private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = null!;
    [Inject] private NavigationManager NavigationManager { get; set; } = null!;

    private PendingShippingCommand<Guid>? _pendingCompletion;
    private PendingShippingCommand<RollbackShippingOrderCommand>? _pendingRollback;
    private PendingPickingOperation? _pendingMovement;
    private bool _isSavingMovement;
    private bool _isChoosingRollback;
    private bool InputsLocked => _pendingRollback is not null || _isSavingMovement || _pendingMovement is not null || _isCompleting
        || _pendingCompletion is not null || _isRollingBack || _isChoosingRollback || _isAcknowledgingSynchronization;
    private ShippingOrder? _order;
    private IReadOnlyList<ShippingOrderItem> SortedItems => _order?.Items
        .OrderBy(x => x.StockKeepingUnit?.Name ?? x.StockKeepingUnitId.ToString(),
            StringComparer.CurrentCultureIgnoreCase)
        .ThenBy(x => x.LineNumber).ToArray() ?? [];
    private MudDataGrid<ShippingOrderItem> _orderItemsGrid = null!;
    private ShippingOrderItem? _selectedLine;
    private int? _expandedLineNumber;
    private List<InventoryMovement> _movements = [];
    private List<PickingSourceLocationAvailability> _availableSourceLocations = [];
    private InventoryMovement? _editingMovement;
    private StorageLocation? _selectedSourceLocation;
    private decimal _movementQuantity;
    private bool _isLoading = true;
    private bool _isCompleting;
    private bool _isRollingBack;
    private bool _isAcknowledgingSynchronization;
    private bool _operationFailed;
    private string? _errorMessage;
    private string? _synchronizationErrorMessage;
    private OrderSynchronizationAssessment? _synchronizationAssessment;
    private IReadOnlyDictionary<string, string> _userNames = new Dictionary<string, string>();

    private bool IsPickingEditable => !InputsLocked
        && (_order?.Status is ShippingOrderStatus.ReadyForPicking
        or ShippingOrderStatus.ReadyForVerification
        or ShippingOrderStatus.InVerification
        or ShippingOrderStatus.Verified);

    private bool CanRollback => _order?.Status is ShippingOrderStatus.ReadyForPicking
        or ShippingOrderStatus.ReadyForVerification
        or ShippingOrderStatus.InVerification
        or ShippingOrderStatus.Verified
        or ShippingOrderStatus.ReadyForShipment;

    private bool CanCompletePicking => IsPickingEditable
        && _synchronizationAssessment?.Level == OrderSynchronizationLevel.Synchronized
        && string.IsNullOrWhiteSpace(_synchronizationErrorMessage);

    private PickingSourceLocationAvailability? SelectedSourceLocationAvailability => _selectedSourceLocation is null
        ? null
        : _availableSourceLocations.FirstOrDefault(x => x.StorageLocation.Id == _selectedSourceLocation.Id);

    private decimal SelectedSourcePhysicalQuantity => SelectedSourceLocationAvailability?.PhysicalQuantity ?? 0;

    private decimal SelectedSourceAvailableQuantity => Math.Max(0,
        (SelectedSourceLocationAvailability?.PhysicalQuantity ?? 0)
        - (SelectedSourceLocationAvailability?.DraftQuantity ?? 0)
        + GetEditedMovementQuantityForSelectedSource());

    private decimal RemainingPlanQuantity => Math.Max(0,
        (_selectedLine?.RemainingQuantity ?? 0) + (_editingMovement?.Quantity ?? 0));

    private decimal MaximumPickingQuantity => Math.Min(SelectedSourceAvailableQuantity, RemainingPlanQuantity);

    private bool CanSaveMovement => IsPickingEditable && _selectedSourceLocation is not null
        && _movementQuantity > 0
        && _movementQuantity <= MaximumPickingQuantity;

    protected override async Task OnParametersSetAsync()
    {
        if (_pendingCompletion is { } pending && pending.Input != Id)
            _pendingCompletion = null;
        if (_pendingMovement is { } movement && movement.OrderId != Id)
            _pendingMovement = null;
        if (_pendingRollback is { } rollback && rollback.Input.OrderId != Id)
            _pendingRollback = null;
        _isLoading = true;
        OperationResult<OrderSynchronizationAssessment> synchronizationResult =
            await SynchronizationService.CheckAsync(Id);
        _synchronizationAssessment = synchronizationResult.Value;
        _synchronizationErrorMessage = synchronizationResult.IsSuccess
            ? null
            : synchronizationResult.Error?.Message
                ?? "Не удалось сверить расходный ордер с 1С.";
        _order = await OrderQueryService.GetOrderAsync(Id);
        _userNames = _order is null
            ? new Dictionary<string, string>()
            : await ApplicationUserQueryService.GetUserNamesAsync([
                _order.PickingStartedBy,
                _order.ReadyForShipmentBy,
                _order.ShippedBy,
                _order.RolledBackBy,
                _order.SynchronizationAcknowledgedBy]);
        _selectedLine = null;
        _expandedLineNumber = null;
        _movements = [];
        _availableSourceLocations = [];
        ResetEditing();
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
                        _synchronizationAssessment = latest.Value;
                }
                return;
            }

            _synchronizationErrorMessage = null;
            _synchronizationAssessment = new OrderSynchronizationAssessment(assessment.Fingerprint, []);
            _order = await OrderQueryService.GetOrderAsync(Id);
        }
        finally { _isAcknowledgingSynchronization = false; }
    }

    private static string FormatDateTime(DateTime? value) =>
        value?.ToLocalTime().ToString("dd.MM.yyyy HH:mm") ?? "—";

    private string GetUserName(string? userId) => string.IsNullOrWhiteSpace(userId)
        ? "—"
        : _userNames.TryGetValue(userId, out string? userName)
            ? userName
            : "Пользователь не найден";

    private async Task ToggleLinePickingAsync(ShippingOrderItem line)
    {
        if (InputsLocked) return;
        if (_expandedLineNumber == line.LineNumber)
        {
            await _orderItemsGrid.ToggleHierarchyVisibilityAsync(line);
            ClearSelectedLine();
            return;
        }

        if (_expandedLineNumber is not null)
        {
            await _orderItemsGrid.CollapseAllHierarchy();
        }

        _operationFailed = false;
        _selectedLine = line;
        _expandedLineNumber = line.LineNumber;
        ResetEditing();
        await LoadSelectedLineDataAsync();
        await _orderItemsGrid.ToggleHierarchyVisibilityAsync(line);
    }

    private void ClearSelectedLine()
    {
        _selectedLine = null;
        _expandedLineNumber = null;
        _movements = [];
        _availableSourceLocations = [];
        ResetEditing();
    }

    private async Task LoadSelectedLineDataAsync()
    {
        if (_selectedLine is null)
        {
            return;
        }

        _movements = await PickingQueryService.GetPickingMovementsAsync(Id, _selectedLine.LineNumber);
        _availableSourceLocations = await PickingQueryService.GetAvailableSourceLocationsAsync(Id, _selectedLine.LineNumber);
    }

    private void BeginEditing(InventoryMovement movement)
    {
        if (!IsPickingEditable)
            return;
        _editingMovement = movement;
        _selectedSourceLocation = movement.SourceStorageLocation;
        _movementQuantity = movement.Quantity;
    }

    private decimal GetEditedMovementQuantityForSelectedSource()
    {
        InventoryMovement? editingMovement = _editingMovement;

        return editingMovement is not null && editingMovement.SourceStorageLocationId == _selectedSourceLocation?.Id
            ? editingMovement.Quantity
            : 0;
    }

    private string FormatSourceLocation(PickingSourceLocationAvailability sourceLocation)
    {
        var unit = _selectedLine?.StockKeepingUnit?.BaseUnitOfMeasure?.Description ?? "—";
        return $"{StorageLocationDisplay.Format(sourceLocation.StorageLocation)} · остаток: {FormatQuantity(sourceLocation.PhysicalQuantity)} {unit} / {WeightDisplay.Format(sourceLocation.PhysicalWeightKg)} · доступно: {FormatQuantity(Math.Max(0, sourceLocation.PhysicalQuantity - sourceLocation.DraftQuantity))} {unit} / {WeightDisplay.Format(sourceLocation.AvailableWeightKg)}";
    }

    private static string FormatQuantity(decimal quantity) => quantity.ToString("N3");

    private void CancelEditing() { if (!InputsLocked) ResetEditing(); }

    private void ResetEditing()
    {
        _editingMovement = null;
        _selectedSourceLocation = null;
        _movementQuantity = 0;
    }

    private Task SaveMovementAsync()
    {
        if (!CanSaveMovement || _selectedLine is null || _selectedSourceLocation is null)
            return Task.CompletedTask;
        if (_editingMovement is null)
        {
            var command = new AddPickingMovementCommand(Id, _selectedLine.LineNumber, _selectedSourceLocation.Id, _movementQuantity);
            return RunMovementAsync("Добавить отбор", context => PickingCommandService.AddPickingMovementAsync(command, context));
        }
        var update = new UpdatePickingMovementCommand(Id, _editingMovement.Id, _selectedSourceLocation.Id, _movementQuantity);
        return RunMovementAsync("Изменить отбор", context => PickingCommandService.UpdatePickingMovementAsync(update, context));
    }

    private Task DeleteMovementAsync(InventoryMovement movement)
    {
        if (!IsPickingEditable) return Task.CompletedTask;
        var command = new DeletePickingMovementCommand(Id, movement.Id);
        return RunMovementAsync("Удалить отбор", context => PickingCommandService.DeletePickingMovementAsync(command, context));
    }

    private async Task RunMovementAsync(string label, Func<CommandContext, Task<OperationResult<Guid>>> execute)
    {
        if (InputsLocked) return;
        var orderId = Id;
        _isSavingMovement = true;
        _operationFailed = false;
        try
        {
            var userId = await GetCurrentUserIdAsync();
            if (userId is null) { SetError("Не удалось определить текущего пользователя."); return; }
            if (Id != orderId) return;
            _pendingMovement = new(orderId, label, new(Guid.NewGuid(), userId), execute);
        }
        catch { SetError("Не удалось определить текущего пользователя."); }
        finally { _isSavingMovement = false; }
        if (_pendingMovement is not null) await RetryMovementAsync();
    }

    private async Task RetryMovementAsync()
    {
        if (_isSavingMovement || _pendingMovement is not { } pending) return;
        _isSavingMovement = true;
        _operationFailed = false;
        try
        {
            if (await GetCurrentUserIdAsync() != pending.Context.UserId)
            {
                SetError("Повторите операцию под пользователем, который её начал.");
                return;
            }
            if (_pendingMovement != pending) return;
            var result = await pending.Execute(pending.Context);
            if (_pendingMovement != pending) return;
            if (result.IsSuccess || result.Error?.Type != OperationErrorType.Failure) _pendingMovement = null;
            if (!result.IsSuccess) { SetError(result.Error?.Message ?? "Не удалось сохранить отбор."); return; }
            ResetEditing();
            await ReloadSelectedLineDataAsync();
        }
        catch { SetError("Не удалось сохранить или обновить отбор."); }
        finally { _isSavingMovement = false; }
    }

    private sealed record PendingPickingOperation(Guid OrderId, string Label, CommandContext Context,
        Func<CommandContext, Task<OperationResult<Guid>>> Execute);

    private async Task ReloadSelectedLineDataAsync()
    {
        if (_selectedLine is null)
        {
            return;
        }

        int lineNumber = _selectedLine.LineNumber;
        _order = await OrderQueryService.GetOrderAsync(Id);
        _selectedLine = _order?.Items.FirstOrDefault(x => x.LineNumber == lineNumber);
        if (_selectedLine is null)
        {
            ClearSelectedLine();
            SetError("Не удалось обновить строку заказа.");
            return;
        }

        await LoadSelectedLineDataAsync();
    }

    private async Task SetReadyForShipmentAsync()
    {
        if (_pendingRollback is not null || _isSavingMovement || _pendingMovement is not null || _isAcknowledgingSynchronization || _isChoosingRollback || _isCompleting || _isRollingBack || (_pendingCompletion is null && !CanCompletePicking))
            return;
        var orderId = Id;
        _isCompleting = true;
        _operationFailed = false;

        try
        {
            string? userId = await GetCurrentUserIdAsync();
            if (userId is null)
            {
                SetError("Не удалось определить текущего пользователя.");
                return;
            }

            if (Id != orderId) return;
            if (_pendingCompletion is { } previous && previous.Context.UserId != userId)
            {
                SetError("Повторите операцию под пользователем, который её начал.");
                return;
            }
            _pendingCompletion ??= new(orderId, new CommandContext(Guid.NewGuid(), userId));
            var pending = _pendingCompletion;
            OperationResult result = await OrderCommandService.SetReadyForShipmentAsync(pending.Input, pending.Context);
            if (_pendingCompletion != pending) return;
            if (result.IsSuccess || result.Error?.Type != OperationErrorType.Failure)
                _pendingCompletion = null;
            if (!result.IsSuccess)
            {
                if (result.Error?.Type == OperationErrorType.Conflict)
                {
                    OperationResult<OrderSynchronizationAssessment> latest =
                        await SynchronizationService.CheckAsync(Id);
                    if (latest.IsSuccess)
                    {
                        _synchronizationAssessment = latest.Value;
                        _synchronizationErrorMessage = null;
                        if (_synchronizationAssessment is { Level: not OrderSynchronizationLevel.Synchronized })
                            return;
                    }
                    else
                    {
                        _synchronizationErrorMessage = latest.Error?.Message
                            ?? "Не удалось сверить расходный ордер с 1С.";
                    }
                }

                SetError(result.Error?.Message ?? "Не удалось подготовить ордер к отгрузке.");
                return;
            }

            NavigationManager.NavigateTo($"/shipping-orders/{Id}");
        }
        catch
        {
            SetError("Не удалось подготовить ордер к отгрузке.");
        }
        finally
        {
            _isCompleting = false;
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
        _operationFailed = false;
        try
        {
            var userId = await GetCurrentUserIdAsync();
            if (userId is null) { SetError("Не удалось определить текущего пользователя."); return; }
            if (Id != orderId) return;
            _pendingRollback = new(command, new CommandContext(Guid.NewGuid(), userId));
        }
        catch { SetError("Не удалось определить текущего пользователя."); }
        finally { _isRollingBack = false; }
        if (_pendingRollback is not null) await RetryRollbackAsync();
    }

    private async Task RetryRollbackAsync()
    {
        if (_isRollingBack || _pendingRollback is not { } pending) return;
        _isRollingBack = true;
        _operationFailed = false;
        try
        {
            if (await GetCurrentUserIdAsync() != pending.Context.UserId)
            {
                SetError("Повторите операцию под пользователем, который её начал.");
                return;
            }
            if (_pendingRollback != pending) return;
            var result = await OrderCommandService.RollbackAsync(pending.Input, pending.Context);
            if (_pendingRollback != pending) return;
            if (result.IsSuccess || result.Error?.Type != OperationErrorType.Failure) _pendingRollback = null;
            if (!result.IsSuccess) { SetError(result.Error?.Message ?? "Не удалось откатить расходный ордер."); return; }
            NavigationManager.NavigateTo("/shipping-orders");
        }
        catch { SetError("Не удалось выполнить или обновить откат расходного ордера."); }
        finally { _isRollingBack = false; }
    }

    private void SetError(string message)
    {
        _operationFailed = true;
        _errorMessage = message;
    }

    private async Task<string?> GetCurrentUserIdAsync()
    {
        AuthenticationState authenticationState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
        return authenticationState.User.FindFirstValue(ClaimTypes.NameIdentifier);
    }
}
