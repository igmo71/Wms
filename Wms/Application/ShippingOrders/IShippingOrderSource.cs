using Wms.Common;
using Wms.Domain;

namespace Wms.Application.ShippingOrders;

public interface IShippingOrderSource
{
    Task<OperationResult<ShippingOrderImportSnapshot>> GetSnapshotAsync(
        Guid orderId,
        CancellationToken ct = default);
}
