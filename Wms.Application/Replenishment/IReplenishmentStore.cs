using Wms.Application.CommandExecution;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.Replenishment;

public sealed record ReplenishmentProposalState(
    IReadOnlyList<ReplenishmentLocation> Locations,
    IReadOnlyList<ReplenishmentTask> ActiveTasks);

public sealed record ReplenishmentSourceStartState(
    ReplenishmentTask? Task, StorageLocation? Location,
    PickingAssembly? Assembly);

public sealed record ReplenishmentTransferState(
    ReplenishmentTask? Task, StorageLocation? Source,
    StorageLocation? Destination,
    IReadOnlySet<Guid> DestinationSkuIds);

public sealed record ReplenishmentQueueEntry(
    Guid Id, Guid PickingAssemblyId, string AssemblyNumber,
    Guid StockKeepingUnitId, string? SkuCode, string? SkuName,
    decimal OrderedQuantity, decimal TransferredQuantity,
    ReplenishmentTaskStatus Status, string? AssignedTo,
    DateTimeOffset OrderedAtUtc, Guid? RecommendedSourceLocationId,
    string? RecommendedSourceCode, Guid? RecommendedDestinationLocationId,
    string? RecommendedDestinationCode,
    IReadOnlyList<ShippingOrderQueue> SourceQueues);

public interface IReplenishmentStore
{
    Task<ReplenishmentSourceStartState> LoadForSourceStartAsync(
        ICommandExecutionContext session, Guid taskId, string locationCode,
        CancellationToken ct);

    Task<ReplenishmentTransferState> LoadForTransferAsync(
        ICommandExecutionContext session, Guid taskId,
        string destinationLocationCode, CancellationToken ct);

    Task<IReadOnlyList<ReplenishmentTask>> LoadOrderedTasksAsync(
        ICommandExecutionContext session, Guid warehouseId, Guid skuId,
        CancellationToken ct);

    Task<IReadOnlySet<Guid>> ReadReservedLocationIdsAsync(
        ICommandExecutionContext session, Guid warehouseId,
        IReadOnlyCollection<Guid> locationIds, CancellationToken ct);

    Task<IReadOnlyList<ReplenishmentQueueEntry>> ReadQueueAsync(
        Guid warehouseId, CancellationToken ct);

    Task<ReplenishmentProposalState> LoadProposalStateAsync(
        ICommandExecutionContext session, Guid warehouseId,
        IReadOnlyCollection<Guid> skuIds, CancellationToken ct);
}
