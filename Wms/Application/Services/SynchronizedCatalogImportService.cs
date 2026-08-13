using Microsoft.Extensions.DependencyInjection;
using Wms.Integration.OneS.Services;

namespace Wms.Application.Services;

public class SynchronizedCatalogImportService(IServiceProvider serviceProvider)
{
    public Task RefreshWarehousesAsync(CancellationToken ct = default) =>
        serviceProvider.GetRequiredService<Catalog_Склады_Service>().ImportListAsync(ct);

    public Task RefreshStockKeepingUnitsAsync(CancellationToken ct = default) =>
        serviceProvider.GetRequiredService<Catalog_Номенклатура_Service>().ImportListAsync(ct);

    public Task RefreshSkuBarcodesAsync(CancellationToken ct = default) =>
        serviceProvider.GetRequiredService<InformationRegister_ШтрихкодыНоменклатуры_Service>().ImportListAsync(ct);

    public Task RefreshUnitsOfMeasureAsync(CancellationToken ct = default) =>
        serviceProvider.GetRequiredService<Catalog_УпаковкиЕдиницыИзмерения_Service>().ImportListAsync(ct);

    public Task RefreshDeliveryDirectionsAsync(CancellationToken ct = default) =>
        serviceProvider.GetRequiredService<Catalog_ЗоныДоставки_Service>().ImportListAsync(ct);
}
