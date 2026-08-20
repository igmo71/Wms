using Wms.Application.Warehouses;
using Wms.Common;
using Wms.Domain;
using Wms.Integration.OneS.Models;

namespace Wms.Integration.OneS.Services;

public class Catalog_Склады_Service(
    OneCClient oneCClient,
    WarehouseService warehouseService)
{
    public async Task<OperationResult> ImportAsync(string refKey, CancellationToken ct = default)
    {
        var uri = Catalog_Склады.GetUri(refKey);

        var serviceResult = await oneCClient.GetValueAsync<RootObject<Catalog_Склады>>(uri, ct);

        if (!serviceResult.IsSuccess)
        {
            return serviceResult;
        }

        var fetchedItem = serviceResult.Value?.Value?[0];

        if (fetchedItem is null)
        {
            return OperationError.Failure("1С вернула некорректный ответ: склад отсутствует.");
        }

        var warehouse = MapToWarehouse(fetchedItem);

        await warehouseService.CreateOrUpdateAsync(warehouse, ct);

        return OperationResult.Success();
    }

    public async Task<OperationResult> ImportListAsync(CancellationToken ct = default)
    {
        var uri = Catalog_Склады.GetListUri;

        var serviceResult = await oneCClient.GetValueAsync<RootObject<Catalog_Склады>>(uri, ct);

        if (!serviceResult.IsSuccess)
            return serviceResult;

        var fetchedItems = serviceResult.Value?.Value;

        if (fetchedItems is null)
            return OperationError.Failure("1С вернула некорректный ответ: список складов отсутствует.");

        foreach (var fetchedItem in fetchedItems)
        {
            var warehouse = MapToWarehouse(fetchedItem);

            await warehouseService.CreateOrUpdateAsync(warehouse, ct);
        }

        return OperationResult.Success();
    }

    private static Warehouse MapToWarehouse(Catalog_Склады fetchedItem)
    {
        return new Warehouse
        {
            Id = fetchedItem.Ref_Key,
            DeletionMark = fetchedItem.DeletionMark,
            Name = fetchedItem.Description
        };
    }
}
