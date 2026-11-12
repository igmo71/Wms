using Wms.Application.CommandExecution;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.Picking;

public sealed record ShippingExchangeState(ShippingExchangeTarget? Target,
    string? PlanSourceSnapshotJson, PickingAssemblyStatus? AssemblyStatus);

public interface IShippingExchangeLease : IAsyncDisposable { }

public interface IShippingExchangeStore
{
    Task<OperationResult<IShippingExchangeLease>> AcquireAsync(
        Guid shippingOrderId, CancellationToken ct);
    Task<ShippingExchangeState> ReadAsync(Guid pickingAssemblyId,
        Guid shippingOrderId, CancellationToken ct);
    Task<ShippingExchangeTarget?> LoadTargetAsync(
        ICommandExecutionContext session, Guid pickingAssemblyId,
        Guid shippingOrderId, CancellationToken ct);
}
