using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;
using System.Security.Claims;
using Wms.Application.Services;
using Wms.Application.Services.Inventory;
using Wms.Common;
using Wms.Domain;

namespace Wms.WebApp.Components.Pages.InventoryCountPages;

public partial class Index
{
    [Inject] private InventoryCountQueryService InventoryCountQueryService { get; set; } = null!;
    [Inject] private ApplicationUserQueryService ApplicationUserQueryService { get; set; } = null!;
    [Inject] private InventoryCountCommandService InventoryCountCommandService { get; set; } = null!;
    [Inject] private WarehouseService WarehouseService { get; set; } = null!;
    [Inject] private NavigationManager NavigationManager { get; set; } = null!;
    [Inject] private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = null!;

    private MudDataGrid<InventoryCount> _dataGrid = null!;
    private Warehouse? _warehouse;
    private bool _isCreating;
    private bool _createFailed;
    private string? _errorMessage;
    private IReadOnlyDictionary<string, string> _userNames = new Dictionary<string, string>();

    private async Task<GridData<InventoryCount>> LoadServerDataAsync(
        GridState<InventoryCount> state,
        CancellationToken cancellationToken)
    {
        var sortDefinition = state.SortDefinitions.FirstOrDefault();
        var result = await InventoryCountQueryService.ListAsync(new ListQuery
        {
            SortBy = sortDefinition?.SortBy,
            SortDescending = sortDefinition?.Descending ?? true,
            Skip = state.Page * state.PageSize,
            Take = state.PageSize
        }, cancellationToken);

        _userNames = await ApplicationUserQueryService.GetUserNamesAsync(
            result.Items.Select(x => x.PostedBy), cancellationToken);

        return new GridData<InventoryCount>
        {
            Items = result.Items,
            TotalItems = result.TotalItems
        };
    }

    private string GetUserName(string? userId) => string.IsNullOrWhiteSpace(userId)
        ? "—"
        : _userNames.TryGetValue(userId, out var userName)
            ? userName
            : "Пользователь не найден";

    private async Task<IEnumerable<Warehouse>> SearchWarehousesAsync(string? searchText, CancellationToken ct)
    {
        var result = await WarehouseService.ListAsync(new ListQuery
        {
            SearchString = searchText,
            SortBy = "Name",
            Take = 10
        }, ct);

        return result.Items;
    }

    private Task OnWarehouseChanged(Warehouse? warehouse)
    {
        _warehouse = warehouse;
        return Task.CompletedTask;
    }

    private async Task CreateAsync()
    {
        if (_warehouse is not Warehouse warehouse)
            return;

        _isCreating = true;
        _createFailed = false;

        try
        {
            var userId = await GetCurrentUserIdAsync();
            if (userId is null)
            {
                _createFailed = true;
                _errorMessage = "Не удалось определить текущего пользователя.";
                return;
            }

            var result = await InventoryCountCommandService.CreateAsync(warehouse.Id, userId);
            if (!result.IsSuccess || result.Value is null)
            {
                _createFailed = true;
                _errorMessage = result.Error?.Message ?? "Не удалось создать инвентаризацию.";
                return;
            }

            NavigationManager.NavigateTo($"inventory-counts/{result.Value.Id}");
        }
        catch
        {
            _createFailed = true;
            _errorMessage = "Не удалось создать инвентаризацию.";
        }
        finally
        {
            _isCreating = false;
        }
    }

    private async Task<string?> GetCurrentUserIdAsync()
    {
        var authenticationState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
        return authenticationState.User.FindFirstValue(ClaimTypes.NameIdentifier);
    }
}
