using Wms.Common;
using Wms.Domain;

namespace Wms.Application.Picking;

public interface IShippingSourceWriter
{
    Task<OperationResult> SetReadyForPickingAsync(Guid sourceId, CancellationToken ct);
    Task<OperationResult> SetPreparedAsync(Guid sourceId, CancellationToken ct);
    OperationResult<bool> TargetMatches(ShippingExchangeTarget target,
        string planSourceSnapshotJson, ShippingSourceSnapshot current);
    OperationResult ValidateTarget(ShippingExchangeTarget target,
        string planSourceSnapshotJson);
    Task<OperationResult> ApplyTargetAsync(ShippingExchangeTarget target,
        string planSourceSnapshotJson, ShippingSourceSnapshot current,
        CancellationToken ct);
    Task<OperationResult> PostAsync(Guid sourceId, CancellationToken ct);
}
