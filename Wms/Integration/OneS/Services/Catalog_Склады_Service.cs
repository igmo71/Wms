using Wms.Application;
using Wms.Domain;
using Wms.Integration.OneS.Models;

namespace Wms.Integration.OneS.Services;

internal class Catalog_Склады_Service(
    OneCClient oneCClient,
    WarehouseService warehouseService)
{
    public async Task ImportAsync(string Ref_Key, CancellationToken ct = default)
    {
        var fetchedItems = await GetAsync(Ref_Key, ct);

        if (fetchedItems is null)
            return;

        var fetchedItem = fetchedItems[0];

        Warehouse newItem = CreateNew(fetchedItem);

        await warehouseService.CreateOrUpdateAsync(newItem, ct);
    }

    private async Task<List<Catalog_Склады>?> GetAsync(string Ref_Key, CancellationToken ct = default)
    {
        var uri = Catalog_Склады.GetUri(Ref_Key);
        var rootObject = await oneCClient.GetValueAsync<RootObject<Catalog_Склады>>(uri, ct);
        var result = rootObject?.Value;

        return result;
    }

    public async Task ImportListAsync(CancellationToken cancellationToken = default)
    {
        var fetchedItems = await GetListAsync(cancellationToken);

        if (fetchedItems is null)
            return;

        foreach (var fetchedItem in fetchedItems)
        {
            var newItem = CreateNew(fetchedItem);

            await warehouseService.CreateOrUpdateAsync(newItem, cancellationToken);
        }
    }

    private async Task<List<Catalog_Склады>?> GetListAsync(CancellationToken ct)
    {
        var uri = Catalog_Склады.GetListUri;
        var rootObject = await oneCClient.GetValueAsync<RootObject<Catalog_Склады>>(uri, ct);
        var result = rootObject?.Value;

        return result;
    }

    private static Warehouse CreateNew(Catalog_Склады fetchedItem)
    {
        return new Warehouse
        {
            Id = fetchedItem.Ref_Key,
            DeletionMark = fetchedItem.DeletionMark,
            Name = fetchedItem.Description
        };
    }
}
