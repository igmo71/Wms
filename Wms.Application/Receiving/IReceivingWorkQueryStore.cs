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
    decimal Quantity, DateTimeOffset CreatedAtUtc, Guid MovementId);

public sealed record ReceivingWorkLpnView(
    Guid Id, string Code, Guid OrderId, Guid WarehouseId,
    LicensePlateNumberStatus Status, string? ResponsibleUserId,
    Guid? OriginalReceivingLocationId, long Revision,
    IReadOnlyList<ReceivingWorkLpnItemView> Items,
    IReadOnlyList<ReceivingWorkPutawayView> Putaways);

public sealed record ReceivingWorkOrderView(
    Guid Id, string? Number, Guid WarehouseId, ReceivingOrderStatus Status,
    int? ActiveCycleNumber, Guid? ReceivingLocationId, bool IsParticipant,
    long Revision, IReadOnlyList<Guid> SourceRefKeys,
    IReadOnlyList<ReceivingWorkItemView> Items,
    IReadOnlyList<ReceivingWorkLpnView> Lpns);

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
    Task<IReadOnlyList<ReceivingPutawayRecommendationView>>
        GetPutawayRecommendationsAsync(
            Guid warehouseId, Guid skuId, CancellationToken ct);
}
