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

    private ReceivingOrder? _order;
    private bool _isLoading = true;
    private bool _updateFailed;

    protected override async Task OnParametersSetAsync()
    {
        _isLoading = true;
        _order = await OrderQueryService.GetOrderAsync(Id);
        _isLoading = false;
    }

    private async Task UpdateFactQuantityAsync(ReceivingOrderItem item, double factQuantity)
    {
        await UpdateOrderItemAsync(item, factQuantity, item.Comment);
    }

    private async Task UpdateCommentAsync(ReceivingOrderItem item, string? comment)
    {
        await UpdateOrderItemAsync(item, item.FactQuantity, comment);
    }

    private async Task UpdateOrderItemAsync(ReceivingOrderItem item, double factQuantity, string? comment)
    {
        _updateFailed = false;

        try
        {
            var updatedItems = await OrderCommandService.UpdateOrderItemFactQuantityAsync(
                item.ReceivingOrderId,
                item.LineNumber,
                factQuantity,
                comment);

            if (updatedItems == 0)
            {
                _updateFailed = true;
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

    private static string FormatDateTimeOffset(DateTimeOffset? value) =>
        value?.ToLocalTime().ToString("dd.MM.yyyy HH:mm") ?? "—";
}
