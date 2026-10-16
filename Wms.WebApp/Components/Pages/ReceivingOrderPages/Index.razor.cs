using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Wms.Application.Receiving;

namespace Wms.WebApp.Components.Pages.ReceivingOrderPages;

public partial class Index
{
    [Inject] private GetReceivingSources Sources { get; set; } = null!;
    [Inject] private CreateReceivingOrder CreateOrder { get; set; } = null!;
    [Inject] private IReceivingWorkQueryStore Orders { get; set; } = null!;
    [Inject] private AuthenticationStateProvider Authentication { get; set; } = null!;
    [Inject] private NavigationManager Navigation { get; set; } = null!;

    private IReadOnlyList<ReceivingSourceView> _sources = [];
    private IReadOnlyList<ReceivingWorkOrderView> _orders = [];
    private readonly HashSet<Guid> _selected = [];
    private string? _orderFilter;
    private string? _error;
    private bool _busy;

    private IEnumerable<ReceivingSourceView> SelectedSources =>
        _sources.Where(x => _selected.Contains(x.RefKey));

    private bool CanCreate => _selected.Count > 0
        && SelectedSources.All(x => x.WarehouseId is not null && x.VersionId is not null)
        && SelectedSources.Select(x => x.WarehouseId).Distinct().Count() == 1;

    private IEnumerable<ReceivingWorkOrderView> FilteredOrders =>
        string.IsNullOrWhiteSpace(_orderFilter) ? _orders
            : _orders.Where(x => x.Id.ToString().Contains(_orderFilter.Trim(), StringComparison.OrdinalIgnoreCase)
                || x.Number?.Contains(_orderFilter.Trim(), StringComparison.OrdinalIgnoreCase) == true);

    private IReadOnlyList<(Guid SkuId, decimal? Quantity)> SelectedPlan
    {
        get
        {
            var sources = SelectedSources.ToArray();
            var skuIds = sources.SelectMany(x => x.PlanBySku.Keys).Distinct().Order().ToArray();
            return skuIds.Select(skuId =>
            {
                decimal? total = 0;
                foreach (var source in sources)
                {
                    if (!source.PlanBySku.TryGetValue(skuId, out var part))
                        continue;
                    if (part is null || total is null)
                    {
                        total = null;
                        continue;
                    }
                    try { total = checked(total.Value + part.Value); }
                    catch (OverflowException) { total = null; }
                }
                return (skuId, total);
            }).ToArray();
        }
    }

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
            _sources = await Sources.ListUnlinkedAsync();
            _orders = await Orders.ListOrdersAsync(null, userId, CancellationToken.None);
            _selected.IntersectWith(_sources.Select(x => x.RefKey));
        }
        catch (Exception exception)
        {
            _error = exception.Message;
        }
        finally { _busy = false; }
    }

    private void ToggleSource(Guid refKey, ChangeEventArgs args)
    {
        if (args.Value is true)
            _selected.Add(refKey);
        else
            _selected.Remove(refKey);
    }

    private async Task CreateAsync()
    {
        if (_busy || !CanCreate)
            return;
        _busy = true;
        _error = null;
        try
        {
            var result = await CreateOrder.ExecuteAsync(_selected.ToArray());
            if (result.IsSuccess)
                Navigation.NavigateTo($"receiving-orders/{result.Value}");
            else
                _error = result.Error!.Message;
        }
        catch (Exception exception)
        {
            _error = exception.Message;
        }
        finally { _busy = false; }
    }
}
