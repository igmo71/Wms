using Wms.Application.SkuBarcodes;
using Wms.Common;
using Wms.Domain;
using Wms.Integration.OneS.Models;

namespace Wms.Integration.OneS.Services;

public class InformationRegister_ШтрихкодыНоменклатуры_Service(
    OneCClient oneCClient,
    SkuBarcodeService skuBarcodeService)
{
    public async Task<OperationResult> ImportAsync(string refKey, CancellationToken ct = default)
    {
        var uri = InformationRegister_ШтрихкодыНоменклатуры.GetUri(refKey);

        var serviceResult = await oneCClient.GetValueAsync<RootObject<InformationRegister_ШтрихкодыНоменклатуры>>(uri, ct);

        if (!serviceResult.IsSuccess)
        {
            return serviceResult;
        }

        var fetchedItems = serviceResult.Value?.Value;

        if (fetchedItems is null)
        {
            return OperationError.Failure("1С вернула некорректный ответ: штрихкоды номенклатуры отсутствуют.");
        }

        if (!Guid.TryParse(refKey, out var skuId))
        {
            return OperationError.Invalid("Некорректный идентификатор номенклатуры в уведомлении 1С.");
        }

        await skuBarcodeService.DeleteRangeAsync(skuId, ct);

        var newItems = fetchedItems
            .Select(x => CreateNew(x))
            .ToList();

        await skuBarcodeService.CreateListAsync(newItems, ct);

        return OperationResult.Success();
    }

    public async Task<OperationResult> ImportListAsync(CancellationToken ct = default)
    {

        var uri = InformationRegister_ШтрихкодыНоменклатуры.GetListUri;

        var serviceResult = await oneCClient.GetValueAsync<RootObject<InformationRegister_ШтрихкодыНоменклатуры>>(uri, ct);

        if (!serviceResult.IsSuccess)
            return serviceResult;

        var fetchedItems = serviceResult.Value?.Value;

        if (fetchedItems is null)
            return OperationError.Failure("1С вернула некорректный ответ: список штрихкодов отсутствует.");

        await skuBarcodeService.DeleteAllAsync(ct);

        var newItems = fetchedItems
            .Select(x => CreateNew(x))
            .ToList();

        await skuBarcodeService.CreateListAsync(newItems, ct);

        return OperationResult.Success();
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
