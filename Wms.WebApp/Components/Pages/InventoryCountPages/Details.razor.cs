using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;
using Wms.Application.Inventory.Counts;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.WebApp.Components.Pages.InventoryCountPages;

public partial class Details
{
    [Parameter] public Guid Id { get; set; }

    [Inject] private InventoryCountQueryService InventoryCountQueryService { get; set; } = null!;
    [Inject] private InventoryCountCommandService InventoryCountCommandService { get; set; } = null!;
    [Inject] private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = null!;
    [Inject] private IDialogService DialogService { get; set; } = null!;
    [Inject] private NavigationManager NavigationManager { get; set; } = null!;
    [Inject] private ILogger<Details> Logger { get; set; } = null!;

    private InventoryCount? _inventoryCount;
    private InventoryCountSkuSearchResult? _selectedSku;
    private double? _manualQuantity;
    private bool _isLoading = true;
    private bool _isBusy;
    private bool _operationFailed;
    private string? _errorMessage;

    private bool IsDraft => _inventoryCount?.Status == InventoryCountStatus.Draft;
    private int CountedItems => _inventoryCount?.Items.Count(x => x.IsCounted) ?? 0;
    private int UncountedItems => _inventoryCount?.Items.Count(x => !x.IsCounted) ?? 0;
    private string LocationText =>
        StorageLocationDisplay.FormatOrDash(_inventoryCount?.StorageLocation);

    protected override Task OnParametersSetAsync() => ReloadAsync();

    private async Task ReloadAsync()
    {
        _isLoading = true;
        _inventoryCount = await InventoryCountQueryService.GetAsync(Id);
        _isLoading = false;
    }

    private async Task<IEnumerable<InventoryCountSkuSearchResult>> SearchSkusAsync(
        string? searchText,
        CancellationToken ct)
    {
        var result = await InventoryCountQueryService.SearchSkusAsync(Id, searchText ?? string.Empty, 10, ct);
        return result.IsSuccess ? result.Value! : [];
    }

    private static string GetSkuText(InventoryCountSkuSearchResult? sku) => sku is null
        ? string.Empty
        : $"{sku.Name} · {sku.Code}";

    private async Task SaveManualQuantityAsync()
    {
        if (_isBusy || _selectedSku is null || _manualQuantity is not double quantity)
            return;

        await RunOperationAsync(
            userId => SetSkuCountedQuantityAsync(_selectedSku.Id, quantity, userId),
            "Не удалось сохранить фактическое количество.");
        if (!_operationFailed)
        {
            _selectedSku = null;
            _manualQuantity = null;
        }
    }

    private async Task<OperationResult> SetSkuCountedQuantityAsync(
        Guid stockKeepingUnitId,
        double countedQuantity,
        string userId)
    {
        var result = await InventoryCountCommandService.SetSkuCountedQuantityAsync(
            Id,
            stockKeepingUnitId,
            countedQuantity,
            userId);
        return result.IsSuccess ? OperationResult.Success() : result.Error!;
    }

    private Task UpdateQuantityAsync(InventoryCountItem item, double? quantity) =>
        quantity is null || _isBusy
            ? Task.CompletedTask
            : RunOperationAsync(
                userId => InventoryCountCommandService.SetCountedQuantityAsync(
                    Id,
                    item.Id,
                    quantity.Value,
                    userId),
                "Не удалось сохранить фактическое количество.");

    private Task RemoveItemAsync(InventoryCountItem item) => _isBusy
        ? Task.CompletedTask
        : RunOperationAsync(
            userId => InventoryCountCommandService.RemoveUnexpectedItemAsync(Id, item.Id, userId),
            "Не удалось удалить ошибочно добавленный товар.");

    private async Task PostAsync()
    {
        if (_isBusy)
            return;

        var confirmed = await DialogService.ShowMessageBoxAsync(
            "Провести инвентаризацию",
            "Остатки ячейки будут приведены к указанному фактическому количеству.",
            yesText: "Провести",
            cancelText: "Отмена");
        if (confirmed != true)
            return;

        await RunOperationAsync(
            userId => InventoryCountCommandService.PostAsync(Id, userId),
            "Не удалось провести инвентаризацию.");
    }

    private async Task DeleteDraftAsync()
    {
        if (_isBusy)
            return;

        var confirmed = await DialogService.ShowMessageBoxAsync(
            "Удалить черновик",
            "Введённые данные будут удалены, а ячейка освобождена.",
            yesText: "Удалить",
            cancelText: "Оставить");
        if (confirmed != true)
            return;
        if (_isBusy)
            return;

        _isBusy = true;
        _operationFailed = false;
        try
        {
            var result = await RunAsCurrentUserAsync(userId =>
                InventoryCountCommandService.DeleteDraftAsync(Id, userId));
            if (result.IsSuccess)
                NavigationManager.NavigateTo("inventory-counts");
            else
            {
                _operationFailed = true;
                _errorMessage = result.Error?.Message ?? "Не удалось удалить черновик.";
            }
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Failed to delete inventory count draft {InventoryCountId}.", Id);
            _operationFailed = true;
            _errorMessage = "Не удалось удалить черновик.";
        }
        finally
        {
            _isBusy = false;
        }
    }

    private async Task RunOperationAsync(
        Func<string, Task<OperationResult>> action,
        string fallbackMessage)
    {
        if (_isBusy)
            return;

        _isBusy = true;
        _operationFailed = false;
        try
        {
            var result = await RunAsCurrentUserAsync(action);
            if (!result.IsSuccess)
            {
                _operationFailed = true;
                _errorMessage = result.Error?.Message ?? fallbackMessage;
                return;
            }
            await ReloadAsync();
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Inventory count operation failed for {InventoryCountId}.", Id);
            _operationFailed = true;
            _errorMessage = fallbackMessage;
        }
        finally
        {
            _isBusy = false;
        }
    }

    private async Task<OperationResult> RunAsCurrentUserAsync(Func<string, Task<OperationResult>> action)
    {
        var authenticationState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
        var userId = authenticationState.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return userId is null
            ? OperationError.Invalid("Не удалось определить текущего пользователя.")
            : await action(userId);
    }

    private static string FormatQuantity(double? quantity) => quantity?.ToString("0.###") ?? "—";
}
