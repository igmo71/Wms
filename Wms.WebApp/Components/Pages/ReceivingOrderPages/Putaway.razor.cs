using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;
using System.Security.Claims;
using Wms.Application.ReceivingOrders;
using Wms.Application.Commands;
using Wms.Common;
using Wms.Application.Users;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.WebApp.Components.Pages.ReceivingOrderPages;

public partial class Putaway
{
    [Parameter] public Guid Id { get; set; }

    [Inject] private ReceivingOrderQueryService OrderQueryService { get; set; } = null!;
    [Inject] private ApplicationUserQueryService ApplicationUserQueryService { get; set; } = null!;
    [Inject] private PutawayQueryService PutawayQueryService { get; set; } = null!;
    [Inject] private PutawayCommandService PutawayCommandService { get; set; } = null!;
    [Inject] private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = null!;
    [Inject] private NavigationManager NavigationManager { get; set; } = null!;

    private ReceivingOrder? _order;
    private MudDataGrid<ReceivingOrderItem> _orderItemsGrid = null!;
    private ReceivingOrderItem? _selectedLine;
    private int? _expandedLineNumber;
    private List<InventoryMovement> _movements = [];
    private InventoryMovement? _editingMovement;
    private StorageLocation? _selectedDestination;
    private decimal _movementQuantity;
    private bool _isLoading = true;
    private bool _isExecuting;
    private PendingPutawayOperation? _pendingOperation;
    private bool InputsLocked => true;
    private bool _operationFailed;
    private string? _errorMessage;
    private IReadOnlyDictionary<string, string> _userNames = new Dictionary<string, string>();

    private bool IsEditable => false;
    private List<InventoryMovement> SelectedLineMovements => _selectedLine is null
        ? []
        : _movements.Where(x => x.RecorderLineNumber == _selectedLine.LineNumber).ToList();
    private decimal MaximumQuantity => _selectedLine is null
        ? 0
        : Math.Max(0, GetRemainingQuantity(_selectedLine) + (_editingMovement?.Quantity ?? 0));
    private bool CanSaveMovement => _selectedDestination is not null
        && _movementQuantity > 0
        && _movementQuantity <= MaximumQuantity;
    private bool CanComplete => _order is not null
        && _order.Items.Any(x => x.FactQuantity > 0)
        && _order.Items.All(x => GetAllocatedQuantity(x.LineNumber) == x.FactQuantity);

    protected override async Task OnParametersSetAsync()
    {
        if (_pendingOperation is { } pending && pending.OrderId != Id)
            _pendingOperation = null;
        _isLoading = true;
        _order = await OrderQueryService.GetOrderAsync(Id);
        _userNames = _order is null
            ? new Dictionary<string, string>()
            : await ApplicationUserQueryService.GetUserNamesAsync([
                _order.StartedBy,
                _order.CompletedBy,
                _order.PutawayStartedBy,
                _order.PutawayCompletedBy]);
        _movements = _order is null ? [] : await PutawayQueryService.GetMovementsAsync(Id);
        ClearSelectedLine();
        _isLoading = false;
    }

    private string GetUserName(string? userId) => string.IsNullOrWhiteSpace(userId)
        ? "—"
        : _userNames.TryGetValue(userId, out var userName)
            ? userName
            : "Пользователь не найден";

    private async Task ToggleLineAsync(ReceivingOrderItem line)
    {
        if (InputsLocked) return;
        if (_expandedLineNumber == line.LineNumber)
        {
            await _orderItemsGrid.ToggleHierarchyVisibilityAsync(line);
            ClearSelectedLine();
            return;
        }

        if (_expandedLineNumber is not null)
            await _orderItemsGrid.CollapseAllHierarchy();

        _operationFailed = false;
        _selectedLine = line;
        _expandedLineNumber = line.LineNumber;
        ResetEditing();
        await _orderItemsGrid.ToggleHierarchyVisibilityAsync(line);
    }

    private void ClearSelectedLine()
    {
        _selectedLine = null;
        _expandedLineNumber = null;
        ResetEditing();
    }

    private void BeginEditing(InventoryMovement movement)
    {
        if (InputsLocked) return;
        _editingMovement = movement;
        _selectedDestination = movement.DestinationStorageLocation;
        _movementQuantity = movement.Quantity;
    }

    private void CancelEditing() { if (!InputsLocked) ResetEditing(); }

    private void ResetEditing()
    {
        _editingMovement = null;
        _selectedDestination = null;
        _movementQuantity = 0;
    }

    private Task<IEnumerable<StorageLocation>> SearchDestinationsAsync(string? searchText, CancellationToken ct) =>
        SearchDestinationsInternalAsync(searchText, ct);

    private async Task<IEnumerable<StorageLocation>> SearchDestinationsInternalAsync(string? searchText, CancellationToken ct)
    {
        if (_order is null)
            return [];

        return await PutawayQueryService.SearchDestinationsAsync(_order.WarehouseId, searchText, ct);
    }

    private static string FormatDestination(StorageLocation? location) =>
        StorageLocationDisplay.Format(location);

    private Task SaveMovementAsync()
    {
        if (InputsLocked || !IsEditable || !CanSaveMovement || _selectedLine is null || _selectedDestination is null)
            return Task.CompletedTask;
        if (_editingMovement is null)
        {
            var command = new AddPutawayMovementCommand(Id, _selectedLine.LineNumber, _selectedDestination.Id, _movementQuantity);
            return RunAsync("Добавить размещение", context => PutawayCommandService.AddMovementAsync(command, context), RefreshMovementsAsync);
        }
        var update = new UpdatePutawayMovementCommand(Id, _editingMovement.Id, _selectedDestination.Id, _movementQuantity);
        return RunAsync("Изменить размещение", context => PutawayCommandService.UpdateMovementAsync(update, context), RefreshMovementsAsync);
    }

    private Task DeleteMovementAsync(InventoryMovement movement)
    {
        if (InputsLocked || !IsEditable) return Task.CompletedTask;
        var command = new DeletePutawayMovementCommand(Id, movement.Id);
        return RunAsync("Удалить размещение", context => PutawayCommandService.DeleteMovementAsync(command, context), RefreshMovementsAsync);
    }

    private async Task RefreshMovementsAsync()
    {
        ResetEditing();
        var orderId = Id;
        var movements = await PutawayQueryService.GetMovementsAsync(orderId);
        if (Id == orderId) _movements = movements;
    }

    private Task CompleteAsync()
    {
        if (InputsLocked || !IsEditable || !CanComplete) return Task.CompletedTask;
        var orderId = Id;
        return RunAsync("Завершить размещение", context => PutawayCommandService.CompleteAsync(orderId, context),
            () =>
            {
                NavigationManager.NavigateTo($"receiving-orders/{orderId}");
                return Task.CompletedTask;
            });
    }

    private async Task RunAsync(string label, Func<CommandContext, Task<OperationResult<Guid>>> execute, Func<Task> onSuccess)
    {
        if (InputsLocked) return;
        var orderId = Id;
        _isExecuting = true;
        _operationFailed = false;
        try
        {
            var userId = await GetCurrentUserIdAsync();
            if (userId is null)
            {
                SetError("Не удалось определить текущего пользователя.");
                return;
            }
            if (Id != orderId) return;
            _pendingOperation = new(orderId, label, new(Guid.NewGuid(), userId), execute, onSuccess);
        }
        catch { SetError("Не удалось определить текущего пользователя."); }
        finally { _isExecuting = false; }
        if (_pendingOperation is not null) await RetryAsync();
    }

    private async Task RetryAsync()
    {
        if (_isExecuting || _pendingOperation is not { } pending) return;
        _isExecuting = true;
        _operationFailed = false;
        try
        {
            if (await GetCurrentUserIdAsync() != pending.Context.UserId)
            {
                SetError("Повторите операцию под пользователем, который её начал.");
                return;
            }
            if (_pendingOperation != pending) return;
            var result = await pending.Execute(pending.Context);
            if (_pendingOperation != pending) return;
            if (result.IsSuccess || result.Error?.Type != OperationErrorType.Failure) _pendingOperation = null;
            if (!result.IsSuccess)
            {
                SetError(result.Error?.Message ?? "Не удалось выполнить операцию размещения.");
                return;
            }
            await pending.OnSuccess();
        }
        catch { SetError("Не удалось выполнить или обновить операцию размещения."); }
        finally { _isExecuting = false; }
    }

    private sealed record PendingPutawayOperation(Guid OrderId, string Label, CommandContext Context,
        Func<CommandContext, Task<OperationResult<Guid>>> Execute, Func<Task> OnSuccess);

    private decimal GetAllocatedQuantity(int lineNumber) =>
        _movements.Where(x => x.RecorderLineNumber == lineNumber).Sum(x => x.Quantity);

    private decimal GetRemainingQuantity(ReceivingOrderItem item) =>
        Math.Max(0, item.FactQuantity!.Value - GetAllocatedQuantity(item.LineNumber));

    private static string FormatQuantity(decimal quantity) => quantity.ToString("0.###");

    private void SetError(string message)
    {
        _operationFailed = true;
        _errorMessage = message;
    }

    private async Task<string?> GetCurrentUserIdAsync()
    {
        var authenticationState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
        return authenticationState.User.FindFirstValue(ClaimTypes.NameIdentifier);
    }
}
