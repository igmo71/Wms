using Wms.Application.RecreivingOrders;
using Wms.Domain;
using Wms.Integration.OneS.Models;
using Document = Wms.Integration.OneS.Models.Document_ПриходныйОрдерНаТовары;

namespace Wms.Integration.OneS.Services;



internal class Document_ПриходныйОрдерНаТовары_ImportService(
    OneCClient oneCClient,
    ReceivingOrderService receivingOrderService)
{
    public async Task ImportAsync(string Ref_Key, CancellationToken ct = default)
    {
        var fetchedItem = await GetAsync(Ref_Key, ct);

        if (fetchedItem is null)
            return;

        ReceivingOrder importedOrder = Document.MapToReceivingOrder(fetchedItem);

        await receivingOrderService.CreateOrUpdateImporttedOrderAsync(importedOrder, ct);
    }

    private async Task<Document?> GetAsync(string Ref_Key, CancellationToken ct = default)
    {
        var uri = Document.GetUri(Ref_Key);

        var rootObject = await oneCClient.GetValueAsync<RootObject<Document>>(uri, ct);

        var result = rootObject?.Value?[0];

        return result;
    }

    internal async Task ImportListAsync(CancellationToken ct)
    {
        throw new NotImplementedException();
    }
}
