using System.Text.Json;
using Wms.Common;
using Wms.Domain;

namespace Wms.Application.Receiving;

internal static class ReceivingWorkAudit
{
    public static OperationResult<WarehouseAuditEvent> Create(
        string eventType, DateTimeOffset occurredAtUtc, string userId,
        Guid orderId, Guid? lpnId, Guid? skuId,
        object? before, object? after, string commandType, Guid requestId) =>
        WarehouseAuditEvent.Create(eventType, occurredAtUtc, userId, null,
            orderId, lpnId, skuId, null,
            before is null ? null : JsonSerializer.Serialize(before),
            after is null ? null : JsonSerializer.Serialize(after),
            commandUserId: userId, commandType: commandType, requestId: requestId);
}
