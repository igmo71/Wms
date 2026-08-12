using Microsoft.AspNetCore.Components;
using Wms.Application.Services;
using Wms.Domain;

namespace Wms.WebApp.Components.Pages.DeliveryDirectionPages;

public partial class Index
{
    [Inject] private DeliveryDirectionService DeliveryDirectionService { get; set; } = null!;
    private List<DeliveryDirection> _items = [];
    private bool _includeDeleted;
    private bool _isLoading = true;

    protected override Task OnInitializedAsync() => LoadAsync();

    private IEnumerable<DeliveryDirection> GetChildren(Guid? parentId) => _items
        .Where(x => x.ParentId == parentId)
        .OrderBy(x => x.Description);

    private async Task LoadAsync()
    {
        _isLoading = true;
        try { _items = await DeliveryDirectionService.ListTreeAsync(_includeDeleted); }
        finally { _isLoading = false; }
    }
    private async Task OnIncludeDeletedChangedAsync(bool value) { _includeDeleted = value; await LoadAsync(); }
}
