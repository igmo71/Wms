using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Wms.Application.Picking;
using Wms.Domain.Enums;

namespace Wms.WebApp.Components.Pages.PickingAssemblyPages;

public partial class Index
{
    [Inject] private IPickingAssemblyQueryStore Assemblies { get; set; } = null!;
    [Inject] private AuthenticationStateProvider Authentication { get; set; } = null!;
    private IReadOnlyList<PickingAssemblyListItem> _items = [];
    private string? _filter;
    private string? _error;
    private bool _busy;
    private bool _isManager;

    private IEnumerable<PickingAssemblyListItem> Filtered =>
        string.IsNullOrWhiteSpace(_filter) ? _items : _items.Where(x =>
            x.Number.Contains(_filter.Trim(), StringComparison.OrdinalIgnoreCase)
            || x.Sources.Any(s => s.Number?.Contains(_filter.Trim(), StringComparison.OrdinalIgnoreCase) == true));

    protected override async Task OnInitializedAsync()
    {
        var principal = (await Authentication.GetAuthenticationStateAsync()).User;
        _isManager = principal.IsInRole("Manager");
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        _busy = true;
        _error = null;
        try
        {
            var items = await Assemblies.ListForManagerAsync(CancellationToken.None);
            _items = _isManager ? items : items.Where(x =>
                x.Status == PickingAssemblyStatus.Closed
                && x.Sources.Any(s =>
                    s.ExchangeStage != ShippingExchangeStage.PostConfirmed)).ToArray();
        }
        catch (Exception exception) { _error = exception.Message; }
        finally { _busy = false; }
    }

    internal static string StatusText(PickingAssemblyStatus status) => status switch
    {
        PickingAssemblyStatus.Draft => "Черновик",
        PickingAssemblyStatus.Preparing => "Подготовка 1С",
        PickingAssemblyStatus.InProgress => "Отбор",
        PickingAssemblyStatus.Closed => "Закрыта",
        PickingAssemblyStatus.CancellationPending => "Отмена ожидается",
        PickingAssemblyStatus.Cancelled => "Отменена",
        _ => status.ToString()
    };
}
