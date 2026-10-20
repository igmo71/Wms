using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Wms.Application.Receiving;

namespace Wms.WebApp.Components.Pages.ReceivingOrderPages;

public partial class Index
{
    [Inject] private IReceivingWorkQueryStore Orders { get; set; } = null!;
    [Inject] private AuthenticationStateProvider Authentication { get; set; } = null!;

    private IReadOnlyList<ReceivingWorkOrderView> _orders = [];
    private string? _orderFilter;
    private string? _error;
    private bool _busy;

    private IEnumerable<ReceivingWorkOrderView> FilteredOrders =>
        string.IsNullOrWhiteSpace(_orderFilter) ? _orders
            : _orders.Where(x => x.Number.Contains(_orderFilter.Trim(), StringComparison.OrdinalIgnoreCase));

    protected override Task OnInitializedAsync() => ReloadAsync();

    private async Task ReloadAsync()
    {
        _busy = true;
        _error = null;
        try
        {
            var userId = (await Authentication.GetAuthenticationStateAsync()).User
                .FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null)
            {
                _error = "Не удалось определить пользователя.";
                return;
            }
            _orders = await Orders.ListOrdersAsync(null, userId, CancellationToken.None);
        }
        catch (Exception exception)
        {
            _error = exception.Message;
        }
        finally { _busy = false; }
    }

}
