using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;
using Wms.Application.Inventory.Counts;
using Wms.Application.Commands;
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
    private IReadOnlyList<InventoryCountItem> SortedItems => _inventoryCount?.Items
        .OrderBy(x => x.StockKeepingUnit?.Name ?? x.StockKeepingUnitId.ToString(),
            StringComparer.CurrentCultureIgnoreCase)
        .ThenBy(x => x.LineNumber).ToArray() ?? [];
    private InventoryCountSkuSearchResult? _selectedSku;
    private decimal? _manualQuantity;
    private bool _isLoading = true;
    private bool _isBusy;
    private bool _isConfirming;
    private PendingCountOperation? _pendingOperation;
    private bool InputsLocked => _isBusy || _isConfirming || _pendingOperation is not null;
    private bool _operationFailed;
    private string? _errorMessage;

    private bool IsDraft => _inventoryCount?.Status == InventoryCountStatus.Draft;
    private int CountedItems => _inventoryCount?.Items.Count(x => x.IsCounted) ?? 0;
    private int UncountedItems => _inventoryCount?.Items.Count(x => !x.IsCounted) ?? 0;
    private string LocationText =>
        StorageLocationDisplay.FormatOrDash(_inventoryCount?.StorageLocation);

    protected override Task OnParametersSetAsync()
    {
        if (_pendingOperation is { } pending && pending.CountId != Id)
            _pendingOperation = null;
        return ReloadAsync();
    }

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

    private Task SaveManualQuantityAsync()
    {
        if (InputsLocked || _selectedSku is null || _manualQuantity is not decimal quantity)
            return Task.CompletedTask;
        var command = new SetInventoryCountSkuQuantityCommand(Id, _selectedSku.Id, quantity);
        return RunOperationAsync("Сохранить количество товара",
            context => InventoryCountCommandService.SetSkuCountedQuantityAsync(command, context),
            async () =>
            {
                _selectedSku = null;
                _manualQuantity = null;
                await ReloadAsync();
            });
    }

    private Task UpdateQuantityAsync(InventoryCountItem item, decimal? quantity)
    {
        if (InputsLocked || quantity is null)
            return Task.CompletedTask;
        var command = new SetInventoryCountQuantityCommand(Id, item.Id, quantity.Value);
        return RunOperationAsync("Сохранить количество строки",
            context => InventoryCountCommandService.SetCountedQuantityAsync(command, context), ReloadAsync);
    }

    private Task RemoveItemAsync(InventoryCountItem item)
    {
        if (InputsLocked)
            return Task.CompletedTask;
        var command = new RemoveInventoryCountItemCommand(Id, item.Id);
        return RunOperationAsync("Удалить добавленный товар",
            context => InventoryCountCommandService.RemoveUnexpectedItemAsync(command, context), ReloadAsync);
    }

    private async Task PostAsync()
    {
        if (InputsLocked)
            return;
        var countId = Id;
        _isConfirming = true;
        bool? confirmed;
        try
        {
            confirmed = await DialogService.ShowMessageBoxAsync(
                "Провести инвентаризацию",
                "Остатки ячейки будут приведены к указанному фактическому количеству.",
                yesText: "Провести", cancelText: "Отмена");
        }
        finally { _isConfirming = false; }
        if (confirmed != true || Id != countId)
            return;
        await RunOperationAsync("Провести инвентаризацию",
            context => InventoryCountCommandService.PostAsync(countId, context), ReloadAsync);
    }

    private async Task DeleteDraftAsync()
    {
        if (InputsLocked)
            return;
        var countId = Id;
        _isConfirming = true;
        bool? confirmed;
        try
        {
            confirmed = await DialogService.ShowMessageBoxAsync(
                "Удалить черновик", "Введённые данные будут удалены, а ячейка освобождена.",
                yesText: "Удалить", cancelText: "Оставить");
        }
        finally { _isConfirming = false; }
        if (confirmed != true || Id != countId)
            return;
        await RunOperationAsync("Удалить черновик",
            context => InventoryCountCommandService.DeleteDraftAsync(countId, context),
            () =>
            {
                NavigationManager.NavigateTo("inventory-counts");
                return Task.CompletedTask;
            });
    }

    private async Task RunOperationAsync(string label,
        Func<CommandContext, Task<OperationResult<Guid>>> execute, Func<Task> onSuccess)
    {
        if (InputsLocked)
            return;
        var countId = Id;
        _isBusy = true;
        _operationFailed = false;
        try
        {
            var userId = await GetCurrentUserIdAsync();
            if (userId is null)
            {
                SetError("Не удалось определить текущего пользователя.");
                return;
            }
            if (Id != countId)
                return;
            _pendingOperation = new(countId, label, new(Guid.NewGuid(), userId), execute, onSuccess);
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Failed to identify inventory count user.");
            SetError("Не удалось определить текущего пользователя.");
        }
        finally { _isBusy = false; }
        if (_pendingOperation is not null)
            await RetryAsync();
    }

    private async Task RetryAsync()
    {
        if (_isBusy || _pendingOperation is not { } pending)
            return;
        _isBusy = true;
        _operationFailed = false;
        try
        {
            if (await GetCurrentUserIdAsync() != pending.Context.UserId)
            {
                SetError("Повторите операцию под пользователем, который её начал.");
                return;
            }
            if (_pendingOperation != pending)
                return;
            var result = await pending.Execute(pending.Context);
            if (_pendingOperation != pending)
                return;
            if (result.IsSuccess || result.Error?.Type != OperationErrorType.Failure)
                _pendingOperation = null;
            if (!result.IsSuccess)
            {
                SetError(result.Error?.Message ?? "Не удалось выполнить операцию инвентаризации.");
                return;
            }
            await pending.OnSuccess();
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Inventory count operation failed for {InventoryCountId}.", pending.CountId);
            SetError("Не удалось выполнить операцию инвентаризации.");
        }
        finally { _isBusy = false; }
    }

    private void SetError(string message) { _operationFailed = true; _errorMessage = message; }

    private async Task<string?> GetCurrentUserIdAsync()
    {
        var authenticationState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
        return authenticationState.User.FindFirstValue(ClaimTypes.NameIdentifier);
    }

    private sealed record PendingCountOperation(Guid CountId, string Label, CommandContext Context,
        Func<CommandContext, Task<OperationResult<Guid>>> Execute, Func<Task> OnSuccess);

    private static string FormatQuantity(decimal? quantity) => quantity?.ToString("N3") ?? "—";
}
