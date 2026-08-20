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
        var result = await ImportListAndGetAsync(ct);

        return result.IsSuccess
            ? OperationResult.Success()
            : OperationResult.Failure(result.Error!);
    }

    internal async Task<OperationResult<IReadOnlyDictionary<Guid, UnitOfMeasure>>> ImportListAndGetAsync(
        CancellationToken ct = default)
    {
        var uri = Catalog_УпаковкиЕдиницыИзмерения.GetListUri;

        var serviceResult = await oneCClient.GetValueAsync<RootObject<Catalog_УпаковкиЕдиницыИзмерения>>(uri, ct);

        if (!serviceResult.IsSuccess)
            return OperationResult<IReadOnlyDictionary<Guid, UnitOfMeasure>>.Failure(serviceResult.Error!);

        var fetchedItems = serviceResult.Value?.Value;

        if (fetchedItems is null)
            return OperationError.Failure("1С вернула некорректный ответ: список единиц измерения отсутствует.");

        var units = fetchedItems
            .Select(MapToUnitOfMeasure)
            .ToList();

        await unitOfMeasureService.CreateOrUpdateBatchAsync(units, ct);

        return units.ToDictionary(x => x.Id);
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
            MeasurementType = fetchedItem.ТипИзмеряемойВеличины,
            Numerator = fetchedItem.Числитель,
            Denominator = fetchedItem.Знаменатель
        };
    }
}
