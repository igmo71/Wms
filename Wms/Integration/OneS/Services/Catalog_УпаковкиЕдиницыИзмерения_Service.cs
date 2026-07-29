using Wms.Application;
using Wms.Domain;
using Wms.Integration.OneS.Models;

namespace Wms.Integration.OneS.Services;

internal class Catalog_УпаковкиЕдиницыИзмерения_Service(
    OneCClient oneCClient,
    UnitOfMeasureService unitOfMeasureService)
{
    public async Task Import(string Ref_Key, CancellationToken ct)
    {
        var fetchedItems = await Get(Ref_Key, ct);

        if (fetchedItems is null)
            return;

        var fetchedItem = fetchedItems[0];

        UnitOfMeasure newItem = CreateNew(fetchedItem);

        await unitOfMeasureService.CreateOrUpdateAsync(newItem, ct);
    }

    private async Task<List<Catalog_УпаковкиЕдиницыИзмерения>?> Get(string Ref_Key, CancellationToken ct)
    {
        var uri = Catalog_УпаковкиЕдиницыИзмерения.GetUri(Ref_Key);
        var rootObject = await oneCClient.GetValueAsync<RootObject<Catalog_УпаковкиЕдиницыИзмерения>>(uri, ct);
        var result = rootObject?.Value;

        return result;
    }

    public async Task ImportList(CancellationToken cancellationToken)
    {
        var fetchedItems = await GetList(cancellationToken);

        if (fetchedItems is null)
            return;

        foreach (var fetchedItem in fetchedItems)
        {
            var newItem = CreateNew(fetchedItem);

            await unitOfMeasureService.CreateOrUpdateAsync(newItem, cancellationToken);
        }

        return;
    }
    private async Task<List<Catalog_УпаковкиЕдиницыИзмерения>?> GetList(CancellationToken ct)
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
