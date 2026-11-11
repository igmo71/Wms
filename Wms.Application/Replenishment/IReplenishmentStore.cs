using Wms.Application.CommandExecution;
using Wms.Common;
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

public sealed record ReplenishmentActiveSourceState(
    ReplenishmentTask? Task, StorageLocation? Source);

public sealed record ReplenishmentTransferCorrectionState(
    ReplenishmentTask Task, ReplenishmentSourceCycle Cycle,
    ReplenishmentTransfer Transfer, decimal EffectiveQuantity,
    int CorrectionCount);

public sealed record ReplenishmentTransferView(
    Guid Id, Guid TaskId, Guid StockKeepingUnitId, string? SkuName,
    string SourceLocationCode, string DestinationLocationCode,
    DateOnly? ExpirationDate, decimal OriginalQuantity,
    decimal EffectiveQuantity, DateTimeOffset ConfirmedAtUtc);

public sealed record ReplenishmentTaskTransferView(
    Guid Id, Guid DestinationLocationId, string DestinationCode,
    DateOnly? ExpirationDate, decimal Quantity, decimal EffectiveQuantity,
    DateTimeOffset ConfirmedAtUtc);

public sealed record ReplenishmentTaskCycleView(
    Guid Id, Guid SourceLocationId, string SourceCode,
    DateTimeOffset StartedAtUtc, DateTimeOffset? CompletedAtUtc,
    bool? SourceWasEmpty, IReadOnlyList<ReplenishmentTaskTransferView> Transfers);

public sealed record ReplenishmentSourceStockView(
    Guid SkuId, string? SkuName, DateOnly? ExpirationDate,
    decimal AccountingQuantity, decimal BlockedQuantity)
{
    public decimal AvailableQuantity => Math.Max(0, AccountingQuantity - BlockedQuantity);
}

public sealed record ReplenishmentTaskDetail(
    Guid Id, Guid PickingAssemblyId, Guid WarehouseId,
    Guid StockKeepingUnitId, string? SkuName, decimal OrderedQuantity,
    decimal TransferredQuantity, ReplenishmentTaskStatus Status,
    string? AssignedTo, DateTimeOffset OrderedAtUtc,
    Guid? RecommendedSourceLocationId, string? RecommendedSourceCode,
    Guid? RecommendedDestinationLocationId, string? RecommendedDestinationCode,
    IReadOnlyList<ReplenishmentTaskCycleView> Cycles,
    IReadOnlyList<ReplenishmentSourceStockView> ActiveSourceStock);

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

    Task<ReplenishmentActiveSourceState> LoadActiveSourceAsync(
        ICommandExecutionContext session, Guid taskId, CancellationToken ct);

    Task<bool> HasUnblockedStockAsync(ICommandExecutionContext session,
        Guid warehouseId, Guid locationId, CancellationToken ct);

    Task<OperationResult> ReleaseSourceHoldAsync(ICommandExecutionContext session,
        Guid warehouseId, Guid locationId, Guid taskId, CancellationToken ct);

    Task<IReadOnlyList<ReplenishmentTask>> LoadForAssemblyAsync(
        ICommandExecutionContext session, Guid assemblyId, CancellationToken ct);

    Task<PickingAssembly?> LoadCancellationAssemblyAsync(
        ICommandExecutionContext session, Guid assemblyId, CancellationToken ct);

    Task<ReplenishmentTransferCorrectionState?> LoadTransferCorrectionAsync(
        ICommandExecutionContext session, Guid transferId, CancellationToken ct);

    Task<IReadOnlyList<ReplenishmentTransferView>> ReadTransfersAsync(
        Guid assemblyId, CancellationToken ct);

    Task<ReplenishmentTaskDetail?> ReadTaskAsync(Guid taskId, CancellationToken ct);

    Task<ReplenishmentProposalState> ReadProposalStateAsync(Guid warehouseId,
        IReadOnlyCollection<Guid> skuIds, CancellationToken ct);

    Task<IReadOnlyList<ReplenishmentTask>> ReadAssemblyTasksAsync(
        Guid assemblyId, CancellationToken ct);

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
