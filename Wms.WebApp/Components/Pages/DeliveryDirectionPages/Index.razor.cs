using Microsoft.AspNetCore.Components;
using Wms.Application.Services;
using Wms.Domain;

namespace Wms.WebApp.Components.Pages.DeliveryDirectionPages;

public partial class Index
{
    [Inject] private DeliveryDirectionService DeliveryDirectionService { get; set; } = null!;
    [Inject] private SynchronizedCatalogImportService SynchronizedCatalogImportService { get; set; } = null!;
    private List<DeliveryDirection> _items = [];
    private bool _includeDeleted;
    private bool _isLoading = true;
    private bool _isImporting;
    private bool _importFailed;

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

    private async Task RefreshFromOneCAsync()
    {
        _isImporting = true;
        _importFailed = false;
        try
        {
            await SynchronizedCatalogImportService.RefreshDeliveryDirectionsAsync();
            await LoadAsync();
        }
        catch
        {
            _importFailed = true;
        }
        finally
        {
            _isImporting = false;
        }
    }
}
