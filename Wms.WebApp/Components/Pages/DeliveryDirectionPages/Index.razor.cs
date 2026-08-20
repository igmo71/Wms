using Microsoft.AspNetCore.Components;
using Wms.Application.DeliveryDirections;
using Wms.Domain;
using Wms.Integration.OneS.Services;

namespace Wms.WebApp.Components.Pages.DeliveryDirectionPages;

public partial class Index
{
    [Inject] private DeliveryDirectionService DeliveryDirectionService { get; set; } = null!;
    [Inject] private Catalog_ЗоныДоставки_Service CatalogImportService { get; set; } = null!;
    private List<DeliveryDirection> _items = [];
    private bool _includeDeleted;
    private bool _isLoading = true;
    private bool _isImporting;
    private bool _importFailed;
    private bool _importSucceeded;
    private string? _importMessage;

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
        _importSucceeded = false;
        _importMessage = null;
        try
        {
            var result = await CatalogImportService.ImportListAsync();
            if (!result.IsSuccess)
            {
                _importFailed = true;
                _importMessage = result.Error?.Message ?? "Не удалось обновить направления доставки из 1С.";
                return;
            }

            await LoadAsync();
            _importSucceeded = true;
            _importMessage = "Направления доставки успешно обновлены из 1С.";
        }
        catch
        {
            _importFailed = true;
            _importMessage = "Не удалось обновить направления доставки из 1С.";
        }
        finally
        {
            _isImporting = false;
        }
    }
}
