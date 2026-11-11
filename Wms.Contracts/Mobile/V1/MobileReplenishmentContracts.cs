namespace Wms.Contracts.Mobile.V1;

public enum MobileReplenishmentTaskStatus
{
    Ordered = 0,
    InProgress = 1,
    Completed = 2,
    Cancelled = 3
}

public sealed record MobileReplenishmentProposalGroupResponse(
    DateOnly? ExpirationDate, decimal Quantity);

public sealed record MobileReplenishmentProposalResponse(
    Guid SkuId, Guid SourceLocationId, string SourceCode,
    Guid? DestinationLocationId, string? DestinationCode,
    decimal Quantity, IReadOnlyList<MobileReplenishmentProposalGroupResponse> Groups);

public sealed record MobileReplenishmentDeficitResponse(Guid SkuId, decimal Quantity);

public sealed record MobilePickingReplenishmentTaskResponse(
    Guid Id, Guid SkuId, decimal OrderedQuantity, decimal TransferredQuantity,
    MobileReplenishmentTaskStatus Status,
    Guid? RecommendedSourceLocationId, string? RecommendedSourceCode,
    Guid? RecommendedDestinationLocationId, string? RecommendedDestinationCode);

public sealed record MobilePickingReplenishmentsResponse(
    Guid PickingAssemblyId, long Revision,
    IReadOnlyList<MobileReplenishmentProposalResponse> Proposals,
    IReadOnlyList<MobileReplenishmentDeficitResponse> Deficits,
    IReadOnlyList<MobilePickingReplenishmentTaskResponse> Tasks);

public sealed record MobileReplenishmentQueueItemResponse(
    Guid Id, Guid PickingAssemblyId, string AssemblyNumber,
    Guid SkuId, string? SkuCode, string? SkuName,
    decimal OrderedQuantity, decimal TransferredQuantity,
    MobileReplenishmentTaskStatus Status, string? AssignedTo,
    DateTimeOffset OrderedAtUtc, string PriorityQueue,
    Guid? RecommendedSourceLocationId, string? RecommendedSourceCode,
    Guid? RecommendedDestinationLocationId, string? RecommendedDestinationCode);

public sealed record MobileReplenishmentTransferResponse(
    Guid Id, Guid DestinationLocationId, string DestinationCode,
    DateOnly? ExpirationDate, decimal OriginalQuantity,
    decimal EffectiveQuantity, DateTimeOffset ConfirmedAtUtc);

public sealed record MobileReplenishmentSourceCycleResponse(
    Guid Id, Guid SourceLocationId, string SourceCode,
    DateTimeOffset StartedAtUtc, DateTimeOffset? CompletedAtUtc,
    bool? SourceWasEmpty, IReadOnlyList<MobileReplenishmentTransferResponse> Transfers);

public sealed record MobileReplenishmentSourceStockResponse(
    Guid SkuId, string? SkuName, DateOnly? ExpirationDate,
    decimal AccountingQuantity, decimal BlockedQuantity)
{
    public decimal AvailableQuantity => Math.Max(0, AccountingQuantity - BlockedQuantity);
}

public sealed record MobileReplenishmentTaskResponse(
    Guid Id, Guid PickingAssemblyId, Guid WarehouseId,
    Guid SkuId, string? SkuName, decimal OrderedQuantity,
    decimal TransferredQuantity, MobileReplenishmentTaskStatus Status,
    string? AssignedTo, DateTimeOffset OrderedAtUtc,
    Guid? RecommendedSourceLocationId, string? RecommendedSourceCode,
    Guid? RecommendedDestinationLocationId, string? RecommendedDestinationCode,
    IReadOnlyList<MobileReplenishmentSourceCycleResponse> Cycles,
    IReadOnlyList<MobileReplenishmentSourceStockResponse> ActiveSourceStock);

public sealed record MobileReplenishmentResourceIdResponse(Guid Id);

public sealed record MobileReplenishmentOrderRequest(
    Guid ClientRequestId, Guid WarehouseId, long ExpectedRevision,
    Guid SkuId, Guid SourceLocationId,
    Guid? ExpectedDestinationLocationId, decimal Quantity);

public sealed record MobileReplenishmentStartSourceRequest(
    Guid ClientRequestId, Guid WarehouseId, string SourceLocationCode);

public sealed record MobileReplenishmentTransferRequest(
    Guid ClientRequestId, Guid WarehouseId, Guid SourceCycleId,
    string DestinationLocationCode, Guid SkuId,
    DateOnly? ExpirationDate, decimal Quantity);

public sealed record MobileReplenishmentShortageRequest(
    Guid ClientRequestId, Guid WarehouseId, Guid SourceCycleId,
    Guid SkuId, DateOnly? ExpirationDate,
    decimal ExpectedQuantity, decimal FoundQuantity, string? Comment = null);

public sealed record MobileReplenishmentCompleteSourceRequest(
    Guid ClientRequestId, Guid WarehouseId, Guid SourceCycleId,
    string ScannedSourceLocationCode, bool SourceWasEmpty);
