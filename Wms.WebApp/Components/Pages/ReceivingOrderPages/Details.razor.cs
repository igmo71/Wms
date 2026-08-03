using Microsoft.AspNetCore.Components;
using Wms.Application.ReceivingOrders;
using Wms.Domain;

namespace Wms.WebApp.Components.Pages.ReceivingOrderPages;

public partial class Details
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
    private bool _isStarting;
    private bool _startOrderFailed;

    protected override async Task OnParametersSetAsync()
    {
        _isLoading = true;
        _order = await OrderQueryService.GetOrderAsync(Id);
        _isLoading = false;
    }

    private static string FormatDateTimeOffset(DateTimeOffset? value) =>
        value?.ToLocalTime().ToString("dd.MM.yyyy HH:mm") ?? "—";

    private async Task StartOrderAsync()
    {
        _isStarting = true;
        _startOrderFailed = false;

        try
        {
            if (await OrderCommandService.StartOrderAsync(Id))
                NavigationManager.NavigateTo($"receiving-orders/{Id}/in-process");
            else
                _startOrderFailed = true;
        }
        catch
        {
            _startOrderFailed = true;
        }
        finally
        {
            _isStarting = false;
        }
    }
}
