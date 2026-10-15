using Microsoft.Extensions.Logging;
using System.Text.Json;
using Wms.Application.Receiving;
using Wms.Application.ReceivingOrders;
using Wms.Common;
using Wms.Domain;
using Wms.Integration.OneS.Models;
using Document = Wms.Integration.OneS.Models.Document_ПриходныйОрдерНаТовары;

namespace Wms.Integration.OneS.Services;

internal sealed class Document_ПриходныйОрдерНаТовары_InboundService(
    SynchronizeReceivingSource synchronizeSource,
    ILogger<Document_ПриходныйОрдерНаТовары_InboundService> logger) : IReceivingOrderSource
{
    public async Task<OperationResult<ReceivingOrderImportSnapshot>> GetSnapshotAsync(
        Guid orderId,
        CancellationToken ct = default)
    {
        var synchronization = await synchronizeSource.ExecuteAsync(orderId, ct);
        if (!synchronization.IsSuccess)
            return synchronization.Error!;

        RootObject<Document>? response;
        try
        {
            response = JsonSerializer.Deserialize<RootObject<Document>>(
                synchronization.Value!.SnapshotJson);
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "1С вернула нечитаемый приходный документ {RefKey}", orderId);
            return OperationError.Failure("1С вернула некорректный JSON приходного документа.");
        }

        IReadOnlyList<Document>? documents = response?.Value;
        if (documents is null || documents.Count != 1 || documents[0].Ref_Key != orderId)
            return OperationError.Failure(
                "1С вернула некорректный ответ: ожидался один приходный ордер.");

        logger.LogDebug("Получен документ {@Document}", documents[0]);
        return Document.MapToImportSnapshot(documents[0]);
    }
}
