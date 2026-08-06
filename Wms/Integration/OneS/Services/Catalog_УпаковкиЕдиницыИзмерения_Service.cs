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
        var uri = Catalog_УпаковкиЕдиницыИзмерения.GetUri(Ref_Key);

        var serviceResult = await oneCClient.GetValueAsync<RootObject<Catalog_УпаковкиЕдиницыИзмерения>>(uri, ct);

        if (!serviceResult.IsSuccess)
            return;

        var fetchedItem = serviceResult.Value?.Value?[0];

        if (fetchedItem is null)
            return;

        var uom = MapToUnitOfMeasure(fetchedItem);

        await unitOfMeasureService.CreateOrUpdateAsync(uom, ct);
    }

    public async Task ImportListAsync(CancellationToken ct = default)
    {
        var uri = Catalog_УпаковкиЕдиницыИзмерения.GetListUri;

        var serviceResult = await oneCClient.GetValueAsync<RootObject<Catalog_УпаковкиЕдиницыИзмерения>>(uri, ct);

        if (!serviceResult.IsSuccess)
            return;

        var fetchedItems = serviceResult.Value?.Value;

        if (fetchedItems is null)
            return;

        foreach (var fetchedItem in fetchedItems)
        {
            var uom = MapToUnitOfMeasure(fetchedItem);

            await unitOfMeasureService.CreateOrUpdateAsync(uom, ct);
        }
    }

    private static UnitOfMeasure MapToUnitOfMeasure(Catalog_УпаковкиЕдиницыИзмерения fetchedItem)
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
