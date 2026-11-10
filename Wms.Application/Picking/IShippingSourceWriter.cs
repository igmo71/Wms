using Wms.Common;

namespace Wms.Application.Picking;

public interface IShippingSourceWriter
{
    Task<OperationResult> SetReadyForPickingAsync(Guid sourceId, CancellationToken ct);
    Task<OperationResult> PostAsync(Guid sourceId, CancellationToken ct);
}
