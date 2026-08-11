using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;
using MudBlazor;
using Wms.Application.Services;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.WebApp.Components.Pages.ShippingOrderPages;

public partial class Picking
{
    [Parameter] public Guid Id { get; set; }

    [Inject] private ShippingOrderQueryService OrderQueryService { get; set; } = null!;
    [Inject] private ShippingOrderCommandService OrderCommandService { get; set; } = null!;
    [Inject] private PickingQueryService PickingQueryService { get; set; } = null!;
    [Inject] private PickingCommandService PickingCommandService { get; set; } = null!;
    [Inject] private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = null!;
    [Inject] private NavigationManager NavigationManager { get; set; } = null!;

    private ShippingOrder? _order;
    private MudDataGrid<ShippingOrderItem> _orderItemsGrid = null!;
    private ShippingOrderItem? _selectedLine;
    private int? _expandedLineNumber;
    private List<InventoryMovement> _movements = [];
    private List<StorageLocation> _availableSourceLocations = [];
    private InventoryMovement? _editingMovement;
    private StorageLocation? _selectedSourceLocation;
    private double _movementQuantity;
    private bool _isLoading = true;
    private bool _isCompleting;
    private bool _operationFailed;
    private string? _errorMessage;

    private bool IsPickingEditable => _order?.Status is ShippingOrderStatus.ReadyForPicking
        or ShippingOrderStatus.ReadyForVerification
        or ShippingOrderStatus.InVerification
        or ShippingOrderStatus.Verified;

    protected override async Task OnParametersSetAsync()
    {
        _isLoading = true;
        _order = await OrderQueryService.GetOrderAsync(Id);
        _selectedLine = null;
        _expandedLineNumber = null;
        _movements = [];
        _availableSourceLocations = [];
        CancelEditing();
        _isLoading = false;
    }

    private static string FormatDateTime(DateTime? value) =>
        value?.ToLocalTime().ToString("dd.MM.yyyy HH:mm") ?? "—";

    private async Task ToggleLinePickingAsync(ShippingOrderItem line)
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
            return;

        _movements = await PickingQueryService.GetPickingMovementsAsync(Id, _selectedLine.LineNumber);
        _availableSourceLocations = await PickingQueryService.GetAvailableSourceLocationsAsync(Id, _selectedLine.LineNumber);
    }

    private void BeginEditing(InventoryMovement movement)
    {
        _editingMovement = movement;
        _selectedSourceLocation = movement.SourceStorageLocation;
        _movementQuantity = movement.Quantity;
    }

    private void CancelEditing()
    {
        _editingMovement = null;
        _selectedSourceLocation = null;
        _movementQuantity = 0;
    }

    private async Task SaveMovementAsync()
    {
        if (_selectedLine is null || _selectedSourceLocation is null)
            return;

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
            CancelEditing();

        await ReloadSelectedLineDataAsync();
    }

    private async Task ReloadSelectedLineDataAsync()
    {
        if (_selectedLine is null)
            return;

        await LoadSelectedLineDataAsync();
        _selectedLine.FactQuantity = _movements.Sum(x => x.Quantity);
    }

    private async Task SetReadyForShipmentAsync()
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
