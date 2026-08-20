using Wms.Application.UnitsOfMeasure;
using Wms.Common;
using Wms.Domain;
using Wms.Integration.OneS.Models;

namespace Wms.Integration.OneS.Services;

public class Catalog_УпаковкиЕдиницыИзмерения_Service(
    OneCClient oneCClient,
    UnitOfMeasureService unitOfMeasureService)
{
    public async Task<OperationResult> ImportAsync(string refKey, CancellationToken ct = default)
    {
        var uri = Catalog_УпаковкиЕдиницыИзмерения.GetUri(refKey);

        var serviceResult = await oneCClient.GetValueAsync<RootObject<Catalog_УпаковкиЕдиницыИзмерения>>(uri, ct);

        if (!serviceResult.IsSuccess)
        {
            return serviceResult;
        }

        var fetchedItem = serviceResult.Value?.Value?[0];

        if (fetchedItem is null)
        {
            return OperationError.Failure("1С вернула некорректный ответ: единица измерения отсутствует.");
        }

        var uom = MapToUnitOfMeasure(fetchedItem);

        await unitOfMeasureService.CreateOrUpdateAsync(uom, ct);

        return OperationResult.Success();
    }

    public async Task<OperationResult> ImportListAsync(CancellationToken ct = default)
    {
        var uri = Catalog_УпаковкиЕдиницыИзмерения.GetListUri;

        var serviceResult = await oneCClient.GetValueAsync<RootObject<Catalog_УпаковкиЕдиницыИзмерения>>(uri, ct);

        if (!serviceResult.IsSuccess)
            return serviceResult;

        var fetchedItems = serviceResult.Value?.Value;

        if (fetchedItems is null)
            return OperationError.Failure("1С вернула некорректный ответ: список единиц измерения отсутствует.");

        foreach (var fetchedItem in fetchedItems)
        {
            var uom = MapToUnitOfMeasure(fetchedItem);

            await unitOfMeasureService.CreateOrUpdateAsync(uom, ct);
        }

        return OperationResult.Success();
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
