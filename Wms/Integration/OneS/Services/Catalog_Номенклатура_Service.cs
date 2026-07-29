using Wms.Application;
using Wms.Domain;
using Wms.Integration.OneS.Models;

namespace Wms.Integration.OneS.Services;

internal class Catalog_Номенклатура_Service(
    OneCClient oneCClient,
    StockKeepingUnitService stockKeepingUnitService)
{
    public async Task<List<Catalog_Номенклатура>?> GetList(CancellationToken ct)
    {
        var uri = Catalog_Номенклатура.GetListUri;
        var rootObject = await oneCClient.GetValueAsync<RootObject<Catalog_Номенклатура>>(uri, ct);
        var result = rootObject?.Value;

        return result;
    }

    public async Task<List<Catalog_Номенклатура>?> Get(string Ref_Key, CancellationToken ct)
    {
        var uri = Catalog_Номенклатура.GetUri(Ref_Key);
        var rootObject = await oneCClient.GetValueAsync<RootObject<Catalog_Номенклатура>>(uri, ct);
        var result = rootObject?.Value;

        return result;
    }

    public async Task Import(string Ref_Key, CancellationToken ct)
    {
        var fetchedItems = await Get(Ref_Key, ct);

        if (fetchedItems is null)
            return;

        var fetchedItem = fetchedItems[0];

        StockKeepingUnit newItem = CreateNew(fetchedItem);

        await stockKeepingUnitService.CreateOrUpdateAsync(newItem, ct);
    }

    internal async Task ImportList(CancellationToken cancellationToken)
    {
        var fetchedItems = await GetList(cancellationToken);

        if (fetchedItems is null)
            return;

        foreach (var fetchedItem in fetchedItems)
        {
            StockKeepingUnit newItem = CreateNew(fetchedItem);

            await stockKeepingUnitService.CreateOrUpdateAsync(newItem, cancellationToken);
        }

        return;
    }

    private static StockKeepingUnit CreateNew(Catalog_Номенклатура fetchedItem)
    {
        return new StockKeepingUnit
        {
            BaseUnitOfMeasureId = fetchedItem.ЕдиницаИзмерения_Key,
            Code = fetchedItem.Code,
            DeletionMark = fetchedItem.DeletionMark,
            Description = fetchedItem.Description,
            Id = fetchedItem.Ref_Key,
            Name = fetchedItem.Description,
            IsFolder = fetchedItem.IsFolder,
            ParentId = fetchedItem.Parent_Key,
            WeightKg = fetchedItem.ВесЧислитель != 0 ? (double)fetchedItem.ВесЧислитель / fetchedItem.ВесЗнаменатель : null
        };
    }
}
