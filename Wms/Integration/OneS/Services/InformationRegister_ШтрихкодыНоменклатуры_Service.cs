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
        var fetchedItems = await GetListAsync(refKey, ct);

        if (fetchedItems is null)
            return;

        await skuBarcodeService.DeleteRangeAsync(Guid.Parse(refKey), ct);

        var newItems = fetchedItems
            .Select(x => CreateNew(x))
            .ToList();

        await skuBarcodeService.CreateListAsync(newItems, ct);
    }

    private async Task<List<InformationRegister_ШтрихкодыНоменклатуры>?> GetListAsync(string refKey, CancellationToken ct)
    {
        var uri = InformationRegister_ШтрихкодыНоменклатуры.GetUri(refKey);

        var rootObject = await oneCClient.GetValueAsync<RootObject<InformationRegister_ШтрихкодыНоменклатуры>>(uri, ct);

        var result = rootObject?.Value;

        return result;
    }

    public async Task ImportListAsync(CancellationToken ct = default)
    {
        var fetchedItems = await GetListAsync(ct);

        if (fetchedItems is null)
            return;

        await skuBarcodeService.DeleteAllAsync(ct);

        var newItems = fetchedItems
            .Select(x => CreateNew(x))
            .ToList();

        await skuBarcodeService.CreateListAsync(newItems, ct);
    }

    private async Task<List<InformationRegister_ШтрихкодыНоменклатуры>?> GetListAsync(CancellationToken ct)
    {
        var uri = InformationRegister_ШтрихкодыНоменклатуры.GetListUri;

        var rootObject = await oneCClient.GetValueAsync<RootObject<InformationRegister_ШтрихкодыНоменклатуры>>(uri, ct);

        var result = rootObject?.Value;

        return result;
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
