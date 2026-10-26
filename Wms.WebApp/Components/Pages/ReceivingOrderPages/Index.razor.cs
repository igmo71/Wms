using Microsoft.AspNetCore.Components;
using Wms.Application.Receiving;
using Wms.Domain.Enums;

namespace Wms.WebApp.Components.Pages.ReceivingOrderPages;

public partial class Index
{
    [Inject] private IReceivingWorkQueryStore Orders { get; set; } = null!;

    private IReadOnlyList<ReceivingWorkOrderListView> _orders = [];
    private string? _orderFilter;
    private string? _error;
    private bool _busy;

    private IEnumerable<ReceivingWorkOrderListView> FilteredOrders =>
        string.IsNullOrWhiteSpace(_orderFilter) ? _orders
            : _orders.Where(x => x.Number.Contains(_orderFilter.Trim(), StringComparison.OrdinalIgnoreCase));

    private static string OrderRowStyle(ReceivingWorkOrderListView order, int _) =>
        order.Status == ReceivingOrderStatus.ReadyForReceiving ? ""
            : order.CurrentDiscrepancyCount > 0
                ? "background-color: var(--mud-palette-warning-hover);"
                : "background-color: var(--mud-palette-success-hover);";

    protected override Task OnInitializedAsync() => ReloadAsync();

    private async Task ReloadAsync()
    {
        _busy = true;
        _error = null;
        try
        {
            _orders = await Orders.ListOrdersAsync(null, CancellationToken.None);
        }
        catch (Exception exception)
        {
            _error = exception.Message;
        }
        finally { _busy = false; }
    }

}
