using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;
using Wms.Application.Services;
using Wms.Application.Services.ShippingOrders;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.WebApp.Components.Pages.ShippingOrderPages;

public partial class Picking
{
    [Parameter] public Guid Id { get; set; }

    [Inject] private ShippingOrderQueryService OrderQueryService { get; set; } = null!;
    [Inject] private ApplicationUserQueryService ApplicationUserQueryService { get; set; } = null!;
    [Inject] private ShippingOrderCommandService OrderCommandService { get; set; } = null!;
    [Inject] private PickingQueryService PickingQueryService { get; set; } = null!;
    [Inject] private PickingCommandService PickingCommandService { get; set; } = null!;
    [Inject] private IDialogService DialogService { get; set; } = null!;
    [Inject] private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = null!;
    [Inject] private NavigationManager NavigationManager { get; set; } = null!;

    private ShippingOrder? _order;
    private MudDataGrid<ShippingOrderItem> _orderItemsGrid = null!;
    private ShippingOrderItem? _selectedLine;
    private int? _expandedLineNumber;
    private List<InventoryMovement> _movements = [];
    private List<PickingSourceLocationAvailability> _availableSourceLocations = [];
    private InventoryMovement? _editingMovement;
    private StorageLocation? _selectedSourceLocation;
    private double _movementQuantity;
    private bool _isLoading = true;
    private bool _isCompleting;
    private bool _isRollingBack;
    private bool _operationFailed;
    private string? _errorMessage;
    private IReadOnlyDictionary<string, string> _userNames = new Dictionary<string, string>();

    private bool IsPickingEditable => _order?.Status is ShippingOrderStatus.ReadyForPicking
        or ShippingOrderStatus.ReadyForVerification
        or ShippingOrderStatus.InVerification
        or ShippingOrderStatus.Verified;

    private bool CanRollback => _order?.Status is ShippingOrderStatus.ReadyForPicking
        or ShippingOrderStatus.ReadyForVerification
        or ShippingOrderStatus.InVerification
        or ShippingOrderStatus.Verified
        or ShippingOrderStatus.ReadyForShipment;

    private PickingSourceLocationAvailability? SelectedSourceLocationAvailability => _selectedSourceLocation is null
        ? null
        : _availableSourceLocations.FirstOrDefault(x => x.StorageLocation.Id == _selectedSourceLocation.Id);

    private double SelectedSourcePhysicalQuantity => SelectedSourceLocationAvailability?.PhysicalQuantity ?? 0;

    private double SelectedSourceAvailableQuantity => Math.Max(0,
        (SelectedSourceLocationAvailability?.PhysicalQuantity ?? 0)
        - (SelectedSourceLocationAvailability?.DraftQuantity ?? 0)
        + GetEditedMovementQuantityForSelectedSource());

    private double RemainingPlanQuantity => Math.Max(0,
        (_selectedLine?.RemainingQuantity ?? 0) + (_editingMovement?.Quantity ?? 0));

    private double MaximumPickingQuantity => Math.Min(SelectedSourceAvailableQuantity, RemainingPlanQuantity);

    private bool CanSaveMovement => _selectedSourceLocation is not null
        && _movementQuantity > 0
        && _movementQuantity <= MaximumPickingQuantity;

    protected override async Task OnParametersSetAsync()
    {
        _isLoading = true;
        _order = await OrderQueryService.GetOrderAsync(Id);
        _userNames = _order is null
            ? new Dictionary<string, string>()
            : await ApplicationUserQueryService.GetUserNamesAsync([
                _order.PickingStartedBy,
                _order.ReadyForShipmentBy,
                _order.ShippedBy,
                _order.RolledBackBy]);
        _selectedLine = null;
        _expandedLineNumber = null;
        _movements = [];
        _availableSourceLocations = [];
        CancelEditing();
        _isLoading = false;
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
        CancelEditing();
        await LoadSelectedLineDataAsync();
        await _orderItemsGrid.ToggleHierarchyVisibilityAsync(line);
    }

    private void ClearSelectedLine()
    {
        _selectedLine = null;
        _expandedLineNumber = null;
        _movements = [];
        _availableSourceLocations = [];
        CancelEditing();
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
        _editingMovement = movement;
        _selectedSourceLocation = movement.SourceStorageLocation;
        _movementQuantity = movement.Quantity;
    }

    private double GetEditedMovementQuantityForSelectedSource()
    {
        var editingMovement = _editingMovement;

        return editingMovement is not null && editingMovement.SourceStorageLocationId == _selectedSourceLocation?.Id
            ? editingMovement.Quantity
            : 0;
    }

    private static string FormatSourceLocation(PickingSourceLocationAvailability sourceLocation) =>
        $"{sourceLocation.StorageLocation.Name} · остаток: {FormatQuantity(sourceLocation.PhysicalQuantity)} / {WeightDisplay.Format(sourceLocation.PhysicalWeightKg)} · доступно: {FormatQuantity(Math.Max(0, sourceLocation.PhysicalQuantity - sourceLocation.DraftQuantity))} / {WeightDisplay.Format(sourceLocation.AvailableWeightKg)}";

    private static string FormatQuantity(double quantity) => quantity.ToString("0.###");

    private void CancelEditing()
    {
        _editingMovement = null;
        _selectedSourceLocation = null;
        _movementQuantity = 0;
    }

    private async Task SaveMovementAsync()
    {
        if (_selectedLine is null || _selectedSourceLocation is null)
        {
            return;
        }

        _operationFailed = false;
        var result = _editingMovement is null
            ? await PickingCommandService.AddPickingMovementAsync(Id, _selectedLine.LineNumber, _selectedSourceLocation.Id, _movementQuantity)
            : await PickingCommandService.UpdatePickingMovementAsync(_editingMovement.Id, _selectedSourceLocation.Id, _movementQuantity);

        if (!result.IsSuccess)
        {
            SetError(result.Error?.Message ?? "Не удалось сохранить отбор.");
            return;
        }

        CancelEditing();
        await ReloadSelectedLineDataAsync();
    }

    private async Task DeleteMovementAsync(InventoryMovement movement)
    {
        _operationFailed = false;
        var result = await PickingCommandService.DeletePickingMovementAsync(movement.Id);
        if (!result.IsSuccess)
        {
            SetError(result.Error?.Message ?? "Не удалось удалить отбор.");
            return;
        }

        if (_editingMovement?.Id == movement.Id)
        {
            CancelEditing();
        }

        await ReloadSelectedLineDataAsync();
    }

    private async Task ReloadSelectedLineDataAsync()
    {
        if (_selectedLine is null)
        {
            return;
        }

        await LoadSelectedLineDataAsync();
        var factResult = _order!.UpdateItemFact(
            _selectedLine.LineNumber,
            _movements.Sum(x => x.Quantity));
        if (!factResult.IsSuccess)
        {
            SetError(factResult.Error?.Message ?? "Не удалось обновить строку на странице.");
        }
    }

    private async Task SetReadyForShipmentAsync()
    {
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

            var result = await OrderCommandService.SetReadyForShipmentAsync(Id, userId);
            if (!result.IsSuccess)
            {
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
        var dialog = await DialogService.ShowAsync<RollbackDialog>("Откатить расходный ордер");
        var dialogResult = await dialog.Result;

        if (dialogResult is null || dialogResult.Canceled || dialogResult.Data is not string reason)
        {
            return;
        }

        _isRollingBack = true;
        _operationFailed = false;

        try
        {
            string? userId = await GetCurrentUserIdAsync();
            if (userId is null)
            {
                SetError("Не удалось определить текущего пользователя.");
                return;
            }

            var result = await OrderCommandService.RollbackAsync(Id, reason, userId);
            if (!result.IsSuccess)
            {
                SetError(result.Error?.Message ?? "Не удалось откатить расходный ордер.");
                return;
            }

            NavigationManager.NavigateTo("/shipping-orders");
        }
        catch
        {
            SetError("Не удалось откатить расходный ордер.");
        }
        finally
        {
            _isRollingBack = false;
        }
    }

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
