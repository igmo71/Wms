using Wms.Application;
using Wms.Domain;
using Wms.Integration.OneS.Models;

namespace Wms.Integration.OneS.Services;

internal class Catalog_УпаковкиЕдиницыИзмерения_Service(
    OneCClient oneCClient,
    UnitOfMeasureService unitOfMeasureService)
{
    public async Task ImportAsync(string Ref_Key, CancellationToken ct = default)
    {
        var fetchedItems = await GetAsync(Ref_Key, ct);

        if (fetchedItems is null)
            return;

        var fetchedItem = fetchedItems[0];

        UnitOfMeasure newItem = CreateNew(fetchedItem);

        await unitOfMeasureService.CreateOrUpdateAsync(newItem, ct);
    }

    private async Task<List<Catalog_УпаковкиЕдиницыИзмерения>?> GetAsync(string Ref_Key, CancellationToken ct = default)
    {
        var uri = Catalog_УпаковкиЕдиницыИзмерения.GetUri(Ref_Key);
        var rootObject = await oneCClient.GetValueAsync<RootObject<Catalog_УпаковкиЕдиницыИзмерения>>(uri, ct);
        var result = rootObject?.Value;

        return result;
    }

    public async Task ImportListAsync(CancellationToken ct = default)
    {
        var fetchedItems = await GetListAsync(ct);

        if (fetchedItems is null)
            return;

        foreach (var fetchedItem in fetchedItems)
        {
            var newItem = CreateNew(fetchedItem);

            await unitOfMeasureService.CreateOrUpdateAsync(newItem, ct);
        }

        return;
    }
    private async Task<List<Catalog_УпаковкиЕдиницыИзмерения>?> GetListAsync(CancellationToken ct = default)
    {
        var uri = Catalog_УпаковкиЕдиницыИзмерения.GetListUri;
        var rootObject = await oneCClient.GetValueAsync<RootObject<Catalog_УпаковкиЕдиницыИзмерения>>(uri, ct);
        var result = rootObject?.Value;

        return result;
    }

    private static UnitOfMeasure CreateNew(Catalog_УпаковкиЕдиницыИзмерения fetchedItem)
    {
        return new UnitOfMeasure
        {
            Id = fetchedItem.Ref_Key,
            Code = fetchedItem.Code,
            Abbreviation = fetchedItem.МеждународноеСокращение,
            DeletionMark = fetchedItem.DeletionMark,
            Description = fetchedItem.Description,
            Name = fetchedItem.НаименованиеПолное,
            Numerator = fetchedItem.Числитель,
            Denominator = fetchedItem.Знаменатель
        };
    }
}
