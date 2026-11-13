using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;
using Wms.Application.Picking;
using Wms.Domain.Enums;

namespace Wms.WebApp.Components.Pages.ShippingSourcePages;

public partial class Index
{
    [SupplyParameterFromQuery(Name = "document")] public Guid? DocumentId { get; set; }
    [Inject] private IShippingSourceQueryStore Sources { get; set; } = null!;
    [Inject] private IPickingAssemblyQueryStore Assemblies { get; set; } = null!;
    [Inject] private AuthenticationStateProvider Authentication { get; set; } = null!;

    private IReadOnlyList<ShippingSourceListItem> _items = [];
    private HashSet<Guid> _accessibleAssemblyIds = [];
    private Guid? _selectedId;
    private string? _filter;
    private string? _error;
    private bool _busy;

    private IEnumerable<ShippingSourceListItem> Filtered =>
        string.IsNullOrWhiteSpace(_filter) ? _items : _items.Where(x =>
            x.Number?.Contains(_filter.Trim(), StringComparison.OrdinalIgnoreCase) == true);

    private ShippingSourceListItem? ActiveSource => _selectedId is Guid id
        ? _items.FirstOrDefault(x => x.Id == id) : null;

    protected override Task OnInitializedAsync() => ReloadAsync();

    protected override void OnParametersSet()
    {
        if (DocumentId is Guid id) _selectedId = id;
    }

    private async Task ReloadAsync()
    {
        _busy = true;
        _error = null;
        try
        {
            var user = (await Authentication.GetAuthenticationStateAsync()).User;
            var assemblies = await Assemblies.ListForManagerAsync(CancellationToken.None);
            _accessibleAssemblyIds = assemblies.Where(x => user.IsInRole("Manager")
                || x.Status == PickingAssemblyStatus.Closed
                    && x.Sources.Any(source =>
                        source.ExchangeStage != ShippingExchangeStage.PostConfirmed))
                .Select(x => x.Id).ToHashSet();
            _items = await Sources.ListAsync(CancellationToken.None);
        }
        catch (Exception exception) { _error = exception.Message; }
        finally { _busy = false; }
    }

    private void OnSourceRowClick(TableRowClickEventArgs<ShippingSourceListItem> args)
    {
        if (args.Item is not null) _selectedId = args.Item.Id;
    }

    private string SourceRowStyle(ShippingSourceListItem source, int _) =>
        _selectedId == source.Id
            ? "background-color: var(--mud-palette-background-gray);" : "";
}
