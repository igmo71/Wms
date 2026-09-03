using Microsoft.Extensions.Logging;
using Wms.Application.ShippingOrders;
using Wms.Common;
using Wms.Domain;
using Wms.Integration.OneS.Models;
using Document = Wms.Integration.OneS.Models.Document_РасходныйОрдерНаТовары;

namespace Wms.Integration.OneS.Services;

internal sealed class Document_РасходныйОрдерНаТовары_InboundService(
    OneCClient oneCClient,
    ILogger<Document_РасходныйОрдерНаТовары_InboundService> logger) : IShippingOrderSource
{
    public async Task<OperationResult<ShippingOrderImportSnapshot>> GetSnapshotAsync(
        Guid orderId,
        CancellationToken ct = default)
    {
        OperationResult<RootObject<Document>?> fetchResult = await oneCClient
            .GetValueAsync<RootObject<Document>>(Document.GetUri(orderId.ToString()), ct);
        if (!fetchResult.IsSuccess)
            return fetchResult.Error!;

        IReadOnlyList<Document>? documents = fetchResult.Value?.Value;
        if (documents is null || documents.Count != 1)
            return OperationError.Failure(
                "1С вернула некорректный ответ: ожидался один расходный ордер.");

        logger.LogDebug("Получен документ {@Document}", documents[0]);
        return Document.MapToImportSnapshot(documents[0]);
    }
}
