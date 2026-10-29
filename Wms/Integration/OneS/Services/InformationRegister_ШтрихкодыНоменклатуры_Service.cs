using Wms.Application.SkuBarcodes;
using Wms.Common;
using Wms.Integration.OneS.Models;

namespace Wms.Integration.OneS.Services;

public class InformationRegister_ШтрихкодыНоменклатуры_Service(
    OneCClient oneCClient,
    SynchronizeSkuBarcodes synchronize)
{
    public Task<OperationResult> ImportAsync(string refKey, CancellationToken ct = default)
    {
        if (!Guid.TryParse(refKey, out var skuId))
            return Task.FromResult<OperationResult>(
                OperationError.Invalid("Некорректный идентификатор SKU в уведомлении 1С."));

        var uri = InformationRegister_ШтрихкодыНоменклатуры.GetUri(skuId.ToString("D"));
        return synchronize.ExecuteAsync(skuId,
            token => ReadSourceAsync(uri,
                "1С вернула некорректный ответ: штрихкоды SKU отсутствуют.", token), ct);
    }

    public Task<OperationResult> ImportListAsync(CancellationToken ct = default) =>
        synchronize.ExecuteAsync(null,
            token => ReadSourceAsync(
                InformationRegister_ШтрихкодыНоменклатуры.GetListUri,
                "1С вернула некорректный ответ: список штрихкодов отсутствует.", token), ct);

    private async Task<OperationResult<IReadOnlyList<SkuBarcodeSourceItem>>> ReadSourceAsync(
        string uri, string missingItemsMessage, CancellationToken ct)
    {
        var result = await oneCClient.GetValueAsync<
            RootObject<InformationRegister_ШтрихкодыНоменклатуры>>(uri, ct);
        if (!result.IsSuccess)
            return result.Error!;

        if (result.Value?.Value is not { } items)
            return OperationError.Failure(missingItemsMessage);

        return OperationResult<IReadOnlyList<SkuBarcodeSourceItem>>.Success(
            items.Select(x => new SkuBarcodeSourceItem(
                x.Номенклатура_Key, x.Штрихкод)).ToArray());
    }
}
