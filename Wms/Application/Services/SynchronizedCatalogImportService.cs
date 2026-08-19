using Microsoft.Extensions.DependencyInjection;
using Wms.Common;
using Wms.Integration.OneS.Services;

namespace Wms.Application.Services;

public class SynchronizedCatalogImportService(IServiceProvider serviceProvider)
{
    public Task<OperationResult> RefreshWarehousesAsync(CancellationToken ct = default) =>
        serviceProvider.GetRequiredService<Catalog_Склады_Service>().ImportListAsync(ct);

    public Task<OperationResult> RefreshStockKeepingUnitsAsync(CancellationToken ct = default) =>
        serviceProvider.GetRequiredService<Catalog_Номенклатура_Service>().ImportListAsync(ct);

    public Task<OperationResult> RefreshPartnersAsync(CancellationToken ct = default) =>
        serviceProvider.GetRequiredService<Catalog_Партнеры_Service>().ImportListAsync(ct);

    public Task<OperationResult> RefreshIndividualsAsync(CancellationToken ct = default) =>
        serviceProvider.GetRequiredService<Catalog_ФизическиеЛица_Service>().ImportListAsync(ct);

    public Task<OperationResult> RefreshOrganizationalUnitsAsync(CancellationToken ct = default) =>
        serviceProvider.GetRequiredService<Catalog_СтруктураПредприятия_Service>().ImportListAsync(ct);

    public Task<OperationResult> RefreshSkuBarcodesAsync(CancellationToken ct = default) =>
        serviceProvider.GetRequiredService<InformationRegister_ШтрихкодыНоменклатуры_Service>().ImportListAsync(ct);

    public Task<OperationResult> RefreshUnitsOfMeasureAsync(CancellationToken ct = default) =>
        serviceProvider.GetRequiredService<Catalog_УпаковкиЕдиницыИзмерения_Service>().ImportListAsync(ct);

    public Task<OperationResult> RefreshDeliveryDirectionsAsync(CancellationToken ct = default) =>
        serviceProvider.GetRequiredService<Catalog_ЗоныДоставки_Service>().ImportListAsync(ct);
}
