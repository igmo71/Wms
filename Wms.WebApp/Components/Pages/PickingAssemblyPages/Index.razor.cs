using Microsoft.AspNetCore.Components;
using Wms.Application.Picking;
using Wms.Domain.Enums;

namespace Wms.WebApp.Components.Pages.PickingAssemblyPages;

public partial class Index
{
    [Inject] private IPickingAssemblyQueryStore Assemblies { get; set; } = null!;
    private IReadOnlyList<PickingAssemblyListItem> _items = [];
    private string? _filter;
    private string? _error;
    private bool _busy;

    private IEnumerable<PickingAssemblyListItem> Filtered =>
        string.IsNullOrWhiteSpace(_filter) ? _items : _items.Where(x =>
            x.Number.Contains(_filter.Trim(), StringComparison.OrdinalIgnoreCase)
            || x.Sources.Any(s => s.Number?.Contains(_filter.Trim(), StringComparison.OrdinalIgnoreCase) == true));

    protected override Task OnInitializedAsync() => ReloadAsync();

    private async Task ReloadAsync()
    {
        _busy = true;
        _error = null;
        try { _items = await Assemblies.ListForManagerAsync(CancellationToken.None); }
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
