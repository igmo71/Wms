using Wms.Domain.Enums;

namespace Wms.Application.Picking;

public sealed record PickingAssemblyListSource(
    Guid SourceRefKey, string? Number, bool StartConfirmed,
    string? StartError, string? SourceCheckError)
{
    public ShippingExchangeStage? ExchangeStage { get; init; }
    public string? ExchangeError { get; init; }
}

public sealed record PickingAssemblyListItem(
    Guid Id, string Number, Guid WarehouseId, PickingAssemblyStatus Status,
    long Revision, string CreatedBy, string ResponsibleUserId,
    string ResponsibleDisplayName, DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    IReadOnlyList<PickingAssemblyListSource> Sources)
{
    public bool HasWarning => Status == PickingAssemblyStatus.Preparing
        || Sources.Any(x => x.StartError is not null || x.SourceCheckError is not null
            || x.ExchangeError is not null
            || Status == PickingAssemblyStatus.Closed
                && x.ExchangeStage != ShippingExchangeStage.PostConfirmed);
    public string? WarehouseName { get; init; }
}

public sealed record PickingAssemblySourceView(
    Guid SourceRefKey, string? Number, ShippingOrderStatus Status,
    bool Posted, bool DeletionMark, DateTimeOffset? LastCheckedAtUtc,
    string? LastCheckError, DateTimeOffset? StartConfirmedAtUtc,
    DateTimeOffset? LastStartAttemptAtUtc, string? LastStartError,
    string PlanSourceSnapshotJson, string? CurrentSourceJson)
{
    public Guid? WarehouseId { get; init; }
    public string? WarehouseName { get; init; }
    public Guid? ReceiverId { get; init; }
    public string? ReceiverName { get; init; }
    public string? Queue { get; init; }
    public DateTime? PlannedShippingDate { get; init; }
    public IReadOnlyList<PickingSourceLineView> CurrentLines { get; init; } = [];
    public ShippingExchangeStage? ExchangeStage { get; init; }
    public string? ExchangeError { get; init; }
    public long? ExchangeRevision { get; init; }
}

public sealed record PickingSourceLineView(int LineNumber, Guid SkuId,
    string? SkuCode, string? SkuName, decimal Quantity);

public sealed record PickingAuditView(DateTimeOffset OccurredAtUtc,
    string EventType, string? ActorUserId, string? BeforeValuesJson,
    string? AfterValuesJson);

public sealed record PickingAllocationView(Guid ShippingOrderId, Guid SkuId,
    decimal Quantity);

public sealed record PickingShortageView(Guid Id, Guid SkuId, string? SkuName,
    string LocationCode, DateOnly? ExpirationDate, decimal ExpectedQuantity,
    decimal FoundQuantity, decimal HeldFactQuantity, decimal BlockedQuantity,
    string? Comment, string ReportedBy, DateTimeOffset ReportedAtUtc,
    DateTimeOffset? ResolvedAtUtc, string? ResolvedBy,
    string? ResolutionReason, Guid? CorrectionMovementId);

public sealed record PickingAssemblyPlanLineView(
    Guid SourceRefKey, Guid SkuId, string? SkuCode, string? SkuName,
    decimal Quantity);

public sealed record PickingAssemblyView(
    Guid Id, string Number, Guid WarehouseId, PickingAssemblyStatus Status,
    long Revision, string CreatedBy, string ResponsibleUserId,
    string ResponsibleDisplayName, DateTimeOffset CreatedAtUtc,
    DateTimeOffset? FirstStartAttemptAtUtc, DateTimeOffset? StartedAtUtc,
    DateTimeOffset? ClosedAtUtc,
    IReadOnlyList<PickingAssemblySourceView> Sources,
    IReadOnlyList<PickingAssemblyPlanLineView> PlanLines)
{
    public string? WarehouseName { get; init; }
}

public sealed record PickingWorkSkuView(Guid SkuId, string? Code,
    string? Name, string? Unit, bool TracksExpirationDate,
    decimal PlannedQuantity, decimal ConfirmedQuantity);

public sealed record PickingWorkEntryView(Guid Id, Guid StorageLocationId,
    string LocationCode, Guid SkuId, DateOnly? ExpirationDate,
    decimal Quantity, string ConfirmedBy, DateTimeOffset ConfirmedAtUtc,
    bool FromReserve);

public sealed record PickingWorkView(Guid Id, Guid WarehouseId,
    PickingAssemblyStatus Status, long Revision, string ResponsibleUserId,
    IReadOnlyList<PickingWorkSkuView> Skus,
    IReadOnlyList<PickingWorkEntryView> Entries);

public interface IPickingAssemblyQueryStore
{
    Task<IReadOnlyList<PickingAssemblyListItem>> ListForManagerAsync(CancellationToken ct);
    Task<IReadOnlyList<PickingAssemblyListItem>> ListForOperatorAsync(
        Guid warehouseId, string userId, CancellationToken ct);
    Task<PickingAssemblyView?> GetAsync(Guid assemblyId, CancellationToken ct);
    Task<PickingWorkView?> GetWorkAsync(Guid assemblyId, CancellationToken ct);
    Task<IReadOnlyList<PickingAuditView>> GetAuditAsync(Guid assemblyId, CancellationToken ct);
    Task<IReadOnlyList<PickingAllocationView>> GetPostedAllocationsAsync(
        Guid assemblyId, CancellationToken ct);
    Task<IReadOnlyList<PickingShortageView>> GetShortagesAsync(
        Guid assemblyId, CancellationToken ct);
    Task<IReadOnlyList<PickingRouteLocation>> GetRouteLocationsAsync(
        Guid warehouseId, IReadOnlyCollection<Guid> locationIds, CancellationToken ct);
}
