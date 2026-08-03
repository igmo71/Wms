using Microsoft.AspNetCore.Components;
using Wms.Application.RecreivingOrders;

namespace Wms.WebApp.Components.Pages.ReceivingOrderPages;

public partial class InProcess
{
    [Parameter]
    public Guid Id { get; set; }

    [Inject]
    private ReceivingOrderService ReceivingOrderService { get; set; } = null!;

    private ReceivingOrderDetails? _order;
    private bool _isLoading = true;
    private bool _updateFailed;

    protected override async Task OnParametersSetAsync()
    {
        _isLoading = true;
        _order = await ReceivingOrderService.GetOrderAsync(Id);
        _isLoading = false;
    }

    private async Task UpdateFactQuantityAsync(ReceivingOrderItemDetails item, double factQuantity)
    {
        await UpdateOrderItemAsync(item, factQuantity, item.Comment);
    }

    private async Task UpdateCommentAsync(ReceivingOrderItemDetails item, string? comment)
    {
        await UpdateOrderItemAsync(item, item.FactQuantity, comment);
    }

    private async Task UpdateOrderItemAsync(ReceivingOrderItemDetails item, double factQuantity, string? comment)
    {
        _updateFailed = false;

        try
        {
            var updatedItems = await ReceivingOrderService.UpdateOrderItemAsync(new ReceivingOrderItemDetails
            {
                ReceivingOrderId = item.ReceivingOrderId,
                LineNumber = item.LineNumber,
                FactQuantity = factQuantity,
                Comment = comment
            });

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
