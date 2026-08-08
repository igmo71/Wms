using Wms.Integration.OneS.Services;

namespace Wms.Application.Services;

public class WarehouseImportService(Catalog_Склады_Service catalogWarehousesService)
{
    public Task RefreshFromOneCAsync(CancellationToken ct = default) =>
        catalogWarehousesService.ImportListAsync(ct);
}
