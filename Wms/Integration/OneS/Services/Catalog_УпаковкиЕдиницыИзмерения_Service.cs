using Wms.Application;
using Wms.Domain;
using Wms.Integration.OneS.Models;

namespace Wms.Integration.OneS.Services;

public class Catalog_УпаковкиЕдиницыИзмерения_Service(
    OneCClient oneCClient,
    UnitOfMeasureService unitOfMeasureService)
{
    public async Task<List<Catalog_УпаковкиЕдиницыИзмерения>?> GetList(CancellationToken ct)
    {
        var uri = Catalog_УпаковкиЕдиницыИзмерения.GetListUri;
        var rootObject = await oneCClient.GetValueAsync<RootObject<Catalog_УпаковкиЕдиницыИзмерения>>(uri, ct);
        var result = rootObject?.Value;

        return result;
    }

    public async Task<List<Catalog_УпаковкиЕдиницыИзмерения>?> Get(string Ref_Key, CancellationToken ct)
    {
        var uri = Catalog_УпаковкиЕдиницыИзмерения.GetUri(Ref_Key);
        var rootObject = await oneCClient.GetValueAsync<RootObject<Catalog_УпаковкиЕдиницыИзмерения>>(uri, ct);
        var result = rootObject?.Value;

        return result;
    }

    public async Task Import(string Ref_Key, CancellationToken ct)
    {
        var items = await Get(Ref_Key, ct);

        if (items is null)
            return;

        var item = items[0];

        var uom = new UnitOfMeasure
        {
            Id = Guid.Parse(item.Ref_Key),
            Code = item.Code,
            Abbreviation = item.МеждународноеСокращение,
            DeletionMark = item.DeletionMark,
            Description = item.Description,
            Name = item.НаименованиеПолное,
            Numerator = item.Числитель,
            Denominator = item.Знаменатель
        };

        await unitOfMeasureService.CreateOrUpdateAsync(uom, ct);
    }
}
