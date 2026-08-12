using Microsoft.AspNetCore.Components;
using MudBlazor;
using Wms.Application.Services;
using Wms.Domain;

namespace Wms.WebApp.Components.Pages.DeliveryDirectionPages;

public partial class Index
{
    [Inject] private DeliveryDirectionService DeliveryDirectionService { get; set; } = null!;
    private List<DeliveryDirection> _items = [];
    private string? _searchString;
    private bool _includeDeleted;
    private bool _isLoading = true;
    private IReadOnlyCollection<TreeItemData<DeliveryDirection>> _treeItems => CreateTreeItems(null);

    protected override Task OnInitializedAsync() => LoadAsync();
    private IReadOnlyCollection<TreeItemData<DeliveryDirection>> CreateTreeItems(Guid? parentId) => _items
        .Where(x => x.ParentId == parentId)
        .OrderBy(x => x.Description)
        .Select(x => new TreeItemData<DeliveryDirection>
        {
            Value = x,
            Text = x.Description,
            Icon = x.IsFolder ? Icons.Material.Filled.Folder : Icons.Material.Filled.LocalShipping,
            Expanded = !string.IsNullOrWhiteSpace(_searchString),
            Children = CreateTreeItems(x.Id)
        })
        .ToList();
    private async Task LoadAsync()
    {
        _isLoading = true;
        try { _items = await DeliveryDirectionService.ListTreeAsync(_searchString, _includeDeleted); }
        finally { _isLoading = false; }
    }
    private async Task OnSearchChangedAsync(string? value) { _searchString = value; await LoadAsync(); }
    private async Task OnIncludeDeletedChangedAsync(bool value) { _includeDeleted = value; await LoadAsync(); }
}
