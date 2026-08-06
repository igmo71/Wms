using Wms.Application;
using Wms.Domain;
using Wms.Integration.OneS.Models;

namespace Wms.Integration.OneS.Services;

internal class InformationRegister_ШтрихкодыНоменклатуры_Service(
    OneCClient oneCClient,
    SkuBarcodeService skuBarcodeService)
{
    public async Task ImportAsync(string refKey, CancellationToken ct)
    {
        var uri = InformationRegister_ШтрихкодыНоменклатуры.GetUri(refKey);

        var serviceResult = await oneCClient.GetValueAsync<RootObject<InformationRegister_ШтрихкодыНоменклатуры>>(uri, ct);

        if (!serviceResult.IsSuccess)
            return;

        var fetchedItems = serviceResult.Value?.Value;

        if (fetchedItems is null)
            return;

        await skuBarcodeService.DeleteRangeAsync(Guid.Parse(refKey), ct);

        var newItems = fetchedItems
            .Select(x => CreateNew(x))
            .ToList();

        await skuBarcodeService.CreateListAsync(newItems, ct);
    }

    public async Task ImportListAsync(CancellationToken ct = default)
    {

        var uri = InformationRegister_ШтрихкодыНоменклатуры.GetListUri;

        var serviceResult = await oneCClient.GetValueAsync<RootObject<InformationRegister_ШтрихкодыНоменклатуры>>(uri, ct);

        if (!serviceResult.IsSuccess)
            return;

        var fetchedItems = serviceResult.Value?.Value;

        if (fetchedItems is null)
            return;

        await skuBarcodeService.DeleteAllAsync(ct);

        var newItems = fetchedItems
            .Select(x => CreateNew(x))
            .ToList();

        await skuBarcodeService.CreateListAsync(newItems, ct);
    }

    private static SkuBarcode CreateNew(InformationRegister_ШтрихкодыНоменклатуры fetchedItem)
    {
        return new SkuBarcode
        {
            SkuId = fetchedItem.Номенклатура_Key,
            Value = fetchedItem.Штрихкод
        };
    }
}
