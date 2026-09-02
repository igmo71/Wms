using Wms.Common;
using Wms.Domain;

namespace Wms.Application.ReceivingOrders;

public interface IReceivingOrderSource
{
    Task<OperationResult<ReceivingOrderImportSnapshot>> GetSnapshotAsync(
        Guid orderId,
        CancellationToken ct = default);
}
