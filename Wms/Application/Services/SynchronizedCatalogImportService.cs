using Microsoft.Extensions.DependencyInjection;
using Wms.Common;
using Wms.Integration.OneS.Services;

namespace Wms.Application.Services;

public class SynchronizedCatalogImportService(IServiceProvider serviceProvider)
{
    public Task<ServiceResult> RefreshWarehousesAsync(CancellationToken ct = default) =>
        serviceProvider.GetRequiredService<Catalog_Склады_Service>().ImportListAsync(ct);

    public Task<ServiceResult> RefreshStockKeepingUnitsAsync(CancellationToken ct = default) =>
        serviceProvider.GetRequiredService<Catalog_Номенклатура_Service>().ImportListAsync(ct);

    public Task<ServiceResult> RefreshSkuBarcodesAsync(CancellationToken ct = default) =>
        serviceProvider.GetRequiredService<InformationRegister_ШтрихкодыНоменклатуры_Service>().ImportListAsync(ct);

    public Task<ServiceResult> RefreshUnitsOfMeasureAsync(CancellationToken ct = default) =>
        serviceProvider.GetRequiredService<Catalog_УпаковкиЕдиницыИзмерения_Service>().ImportListAsync(ct);

    public Task<ServiceResult> RefreshDeliveryDirectionsAsync(CancellationToken ct = default) =>
        serviceProvider.GetRequiredService<Catalog_ЗоныДоставки_Service>().ImportListAsync(ct);
}
