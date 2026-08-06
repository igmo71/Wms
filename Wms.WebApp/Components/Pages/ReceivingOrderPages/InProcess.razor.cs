using Microsoft.AspNetCore.Components;
using Wms.Application.ReceivingOrders;
using Wms.Domain;

namespace Wms.WebApp.Components.Pages.ReceivingOrderPages;

public partial class InProcess
{
    [Parameter]
    public Guid Id { get; set; }

    [Inject]
    private ReceivingOrderQueryService OrderQueryService { get; set; } = null!;

    [Inject]
    private ReceivingOrderCommandService OrderCommandService { get; set; } = null!;

    [Inject]
    private NavigationManager NavigationManager { get; set; } = null!;

    private ReceivingOrder? _order;
    private bool _isLoading = true;
    private bool _updateFailed;
    private string? _updateErrorMessage;
    private bool _isCompleting;
    private bool _completeFailed;
    private string? _completeErrorMessage;

    protected override async Task OnParametersSetAsync()
    {
        _isLoading = true;
        _order = await OrderQueryService.GetOrderAsync(Id);
        _isLoading = false;
    }

    private async Task UpdateFactQuantityAsync(ReceivingOrderItem item, decimal factQuantity)
    {
        await UpdateOrderItemAsync(item, factQuantity, item.Comment);
    }

    private async Task UpdateCommentAsync(ReceivingOrderItem item, string? comment)
    {
        await UpdateOrderItemAsync(item, item.FactQuantity, comment);
    }

    private async Task UpdateOrderItemAsync(ReceivingOrderItem item, decimal factQuantity, string? comment)
    {
        _updateFailed = false;

        try
        {
            var updateResult = await OrderCommandService.UpdateOrderItemFactQuantityAsync(
                item.ReceivingOrderId,
                item.LineNumber,
                factQuantity,
                comment);

            if (!updateResult.IsSuccess)
            {
                _updateFailed = true;
                _updateErrorMessage = updateResult.Error?.Message ?? "Не удалось обновить количество по факту.";
                return;
            }

            item.FactQuantity = factQuantity;
            item.Comment = comment;
        }
        catch
        {
            _updateFailed = true;
        }
    }

    private async Task CompleteOrderAsync()
    {
        _isCompleting = true;
        _completeFailed = false;

        try
        {
            var result = await OrderCommandService.CompleteOrderAsync(Id);
            if (result.IsSuccess)
                NavigationManager.NavigateTo($"receiving-orders/{Id}");
            else
            {
                _completeFailed = true;
                _completeErrorMessage = result.Error?.Message ?? "Не удалось завершить приходный ордер.";
            }
        }
        catch
        {
            _completeFailed = true;
        }
        finally
        {
            _isCompleting = false;
        }
    }

    private static string FormatDateTimeOffset(DateTimeOffset? value) =>
        value?.ToLocalTime().ToString("dd.MM.yyyy HH:mm") ?? "—";
}
