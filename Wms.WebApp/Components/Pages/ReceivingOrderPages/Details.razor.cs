using Microsoft.AspNetCore.Components;
using Wms.Application.ReceivingOrders;
using Wms.Domain;

namespace Wms.WebApp.Components.Pages.ReceivingOrderPages;

public partial class Details
{
    [Parameter] public Guid Id { get; set; }

    [Inject] private ReceivingOrderQueryService OrderQueryService { get; set; } = null!;

    private ReceivingOrder? _order;
    private bool _isLoading = true;

    protected override async Task OnParametersSetAsync() => await LoadOrderAsync();

    private async Task LoadOrderAsync()
    {
        _isLoading = true;
        _order = await OrderQueryService.GetOrderAsync(Id);
        _isLoading = false;
    }

    private Task ReloadAfterCompletionAsync() => LoadOrderAsync();
}
