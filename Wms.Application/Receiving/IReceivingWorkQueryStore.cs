using Wms.Domain.Enums;

namespace Wms.Application.Receiving;

public sealed record ReceivingWorkItemView(
    Guid SkuId, string? SkuCode, string? SkuName, bool TracksExpirationDate,
    string? UnitOfMeasure, decimal? PlanQuantity, decimal? FactQuantity);

public sealed record ReceivingWorkLpnItemView(
    Guid Id, Guid SkuId, string? SkuCode, string? SkuName,
    string? UnitOfMeasure, decimal Quantity, DateOnly? ExpirationDate,
    decimal PlacedQuantity);

public sealed record ReceivingWorkPutawayView(
    Guid Id, Guid ItemId, Guid DestinationStorageLocationId,
    decimal Quantity, DateTimeOffset CreatedAtUtc, Guid MovementId)
{
    public string? DestinationAddress { get; init; }
}

public sealed record ReceivingWorkLpnView(
    Guid Id, string Code, Guid OrderId, Guid WarehouseId,
    LicensePlateNumberStatus Status, string? ResponsibleUserId,
    Guid? OriginalReceivingLocationId, long Revision,
    IReadOnlyList<ReceivingWorkLpnItemView> Items,
    IReadOnlyList<ReceivingWorkPutawayView> Putaways);

public sealed record ReceivingLpnReversalHistoryView(
    Guid LpnId, Guid MovementId, Guid OriginalMovementId,
    string? SourceAddress, string? DestinationAddress,
    Guid SkuId, DateOnly? ExpirationDate, decimal Quantity,
    DateTimeOffset? PostedAtUtc, string? ConfirmedBy);

public sealed record ReceivingWorkCycleItemView(
    Guid SkuId, decimal? PlanQuantity, decimal? FactQuantity);

public sealed record ReceivingWorkCycleSourceView(Guid RefKey, Guid VersionId);

public sealed record ReceivingWorkCycleView(
    int Number, Guid? ReceivingLocationId, DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc, string? CompletedBy, bool SourceCheckFailed,
    IReadOnlyList<ReceivingWorkCycleItemView> Items,
    IReadOnlyList<ReceivingWorkCycleSourceView> Sources)
{
    public string? ReceivingLocationAddress { get; init; }
}

public sealed record ReceivingWorkOrderView(
    Guid Id, string Number, DateTimeOffset CreatedAtUtc,
    Guid WarehouseId, ReceivingOrderStatus Status,
    int ActiveCycleNumber, Guid? ReceivingLocationId, bool IsParticipant,
    long Revision, IReadOnlyList<Guid> SourceRefKeys,
    IReadOnlyList<ReceivingWorkItemView> Items,
    IReadOnlyList<ReceivingWorkLpnView> Lpns,
    IReadOnlyList<ReceivingWorkCycleView> Cycles)
{
    public string? WarehouseName { get; init; }
    public string? ReceivingLocationAddress { get; init; }

    public int CurrentDiscrepancyCount => Items.Count(x =>
        x.PlanQuantity is null || x.FactQuantity is null
        || x.PlanQuantity != x.FactQuantity);

    public bool HasNewDiscrepancyAfterCompletion
    {
        get
        {
            if (Status != ReceivingOrderStatus.Received)
                return false;
            var last = Cycles.Where(x => x.CompletedAtUtc is not null)
                .MaxBy(x => x.Number);
            if (last is null)
                return false;
            var old = last.Items.ToDictionary(x => x.SkuId);
            return Items.Any(item =>
                (item.PlanQuantity is null || item.FactQuantity is null
                    || item.PlanQuantity != item.FactQuantity)
                && (!old.TryGetValue(item.SkuId, out var snapshot)
                    || snapshot.PlanQuantity != item.PlanQuantity
                    || snapshot.FactQuantity != item.FactQuantity));
        }
    }
}

public sealed record ReceivingPutawayRecommendationView(
    Guid StorageLocationId, string Address, string Name,
    decimal? CurrentSkuQuantity);

public interface IReceivingWorkQueryStore
{
    Task<IReadOnlyList<ReceivingWorkOrderView>> ListOrdersAsync(
        Guid? warehouseId, string userId, CancellationToken ct);
    Task<ReceivingWorkOrderView?> GetOrderAsync(
        Guid orderId, string userId, CancellationToken ct);
    Task<ReceivingWorkOrderView?> GetOrderBySourceAsync(
        Guid refKey, string userId, CancellationToken ct);
    Task<ReceivingWorkLpnView?> GetLpnAsync(
        Guid? lpnId, string? code, CancellationToken ct);
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<ReceivingLpnReversalHistoryView>>>
        GetDeletionHistoryAsync(Guid orderId, CancellationToken ct);
    Task<IReadOnlyList<ReceivingPutawayRecommendationView>>
        GetPutawayRecommendationsAsync(
            Guid warehouseId, Guid skuId, CancellationToken ct);
}
