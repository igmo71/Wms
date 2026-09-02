using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;
using System.Security.Claims;
using Wms.Application.ReceivingOrders;
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
    private double _movementQuantity;
    private bool _isLoading = true;
    private bool _isCompleting;
    private bool _operationFailed;
    private string? _errorMessage;
    private IReadOnlyDictionary<string, string> _userNames = new Dictionary<string, string>();

    private bool IsEditable => _order?.PutawayStatus == PutawayStatus.InProgress;
    private List<InventoryMovement> SelectedLineMovements => _selectedLine is null
        ? []
        : _movements.Where(x => x.RecorderLineNumber == _selectedLine.LineNumber).ToList();
    private double MaximumQuantity => _selectedLine is null
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
        CancelEditing();
        await _orderItemsGrid.ToggleHierarchyVisibilityAsync(line);
    }

    private void ClearSelectedLine()
    {
        _selectedLine = null;
        _expandedLineNumber = null;
        CancelEditing();
    }

    private void BeginEditing(InventoryMovement movement)
    {
        _editingMovement = movement;
        _selectedDestination = movement.DestinationStorageLocation;
        _movementQuantity = movement.Quantity;
    }

    private void CancelEditing()
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

    private async Task SaveMovementAsync()
    {
        if (_selectedLine is null || _selectedDestination is null)
            return;

        _operationFailed = false;
        var result = _editingMovement is null
            ? await PutawayCommandService.AddMovementAsync(
                Id, _selectedLine.LineNumber, _selectedDestination.Id, _movementQuantity)
            : await PutawayCommandService.UpdateMovementAsync(
                _editingMovement.Id, _selectedDestination.Id, _movementQuantity);

        if (!result.IsSuccess)
        {
            SetError(result.Error?.Message ?? "Не удалось сохранить размещение.");
            return;
        }

        CancelEditing();
        await ReloadMovementsAsync();
    }

    private async Task DeleteMovementAsync(InventoryMovement movement)
    {
        _operationFailed = false;
        var result = await PutawayCommandService.DeleteMovementAsync(movement.Id);
        if (!result.IsSuccess)
        {
            SetError(result.Error?.Message ?? "Не удалось удалить размещение.");
            return;
        }

        if (_editingMovement?.Id == movement.Id)
            CancelEditing();

        await ReloadMovementsAsync();
    }

    private async Task ReloadMovementsAsync() =>
        _movements = await PutawayQueryService.GetMovementsAsync(Id);

    private async Task CompleteAsync()
    {
        _isCompleting = true;
        _operationFailed = false;

        try
        {
            var userId = await GetCurrentUserIdAsync();
            if (userId is null)
            {
                SetError("Не удалось определить текущего пользователя.");
                return;
            }

            var result = await PutawayCommandService.CompleteAsync(Id, userId);
            if (!result.IsSuccess)
            {
                SetError(result.Error?.Message ?? "Не удалось завершить размещение.");
                return;
            }

            NavigationManager.NavigateTo($"receiving-orders/{Id}");
        }
        catch
        {
            SetError("Не удалось завершить размещение.");
        }
        finally
        {
            _isCompleting = false;
        }
    }

    private double GetAllocatedQuantity(int lineNumber) =>
        _movements.Where(x => x.RecorderLineNumber == lineNumber).Sum(x => x.Quantity);

    private double GetRemainingQuantity(ReceivingOrderItem item) =>
        Math.Max(0, item.FactQuantity!.Value - GetAllocatedQuantity(item.LineNumber));

    private static string FormatQuantity(double quantity) => quantity.ToString("0.###");

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
