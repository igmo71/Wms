namespace Wms.Contracts.Mobile.V1;

public enum MobilePickingAssemblyStatus
{
    Draft = 0,
    Preparing = 1,
    InProgress = 2,
    Closed = 3,
    CancellationPending = 4,
    Cancelled = 5
}

public enum MobileShippingExchangeStage
{
    PendingPatch = 0,
    PatchConfirmed = 1,
    PostConfirmed = 2,
    NeedsReview = 3
}

public sealed record MobilePickingListSourceResponse(
    Guid SourceRefKey, string? Number, bool StartConfirmed,
    string? StartError, string? SourceCheckError)
{
    public MobileShippingExchangeStage? ExchangeStage { get; init; }
    public string? ExchangeError { get; init; }
}

public sealed record MobilePickingAssemblyListResponse(
    Guid Id, string Number, Guid WarehouseId, MobilePickingAssemblyStatus Status,
    long Revision, string CreatedBy, string ResponsibleUserId,
    string ResponsibleDisplayName, DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StartedAtUtc, bool HasWarning,
    IReadOnlyList<MobilePickingListSourceResponse> Sources);

public sealed record MobilePickingSourceResponse(
    Guid SourceRefKey, string? Number, string Status,
    bool Posted, bool DeletionMark, DateTimeOffset? LastCheckedAtUtc,
    string? LastCheckError, DateTimeOffset? StartConfirmedAtUtc,
    DateTimeOffset? LastStartAttemptAtUtc, string? LastStartError,
    string PlanSourceSnapshotJson, string? CurrentSourceJson)
{
    public MobileShippingExchangeStage? ExchangeStage { get; init; }
    public string? ExchangeError { get; init; }
}

public sealed record MobilePickingPlanLineResponse(
    Guid SourceRefKey, Guid SkuId, string? SkuCode, string? SkuName,
    decimal Quantity);

public sealed record MobilePickingAssemblyResponse(
    Guid Id, string Number, Guid WarehouseId, MobilePickingAssemblyStatus Status,
    long Revision, string CreatedBy, string ResponsibleUserId,
    string ResponsibleDisplayName, DateTimeOffset CreatedAtUtc,
    DateTimeOffset? FirstStartAttemptAtUtc, DateTimeOffset? StartedAtUtc,
    DateTimeOffset? ClosedAtUtc,
    IReadOnlyList<MobilePickingSourceResponse> Sources,
    IReadOnlyList<MobilePickingPlanLineResponse> PlanLines);

public sealed record MobilePickingFreeSourceResponse(
    Guid RefKey, string? Number, Guid WarehouseId, string Status);

public sealed record MobilePickingSourceResolutionResponse(
    MobilePickingAssemblyResponse? Assembly,
    MobilePickingFreeSourceResponse? FreeSource);

public sealed record MobilePickingSourceStartResponse(
    Guid SourceRefKey, bool Confirmed, string? Error);

public sealed record MobilePickingStartResponse(
    Guid PickingAssemblyId, MobilePickingAssemblyStatus Status,
    IReadOnlyList<MobilePickingSourceStartResponse> Sources);

public sealed record MobilePickingCommandIdResponse(Guid AssemblyId);

public sealed record MobilePickingExchangeDocumentResponse(
    Guid SourceRefKey, MobileShippingExchangeStage Stage, string? Error);

public sealed record MobilePickingCloseResponse(Guid AssemblyId,
    bool AllConfirmed, IReadOnlyList<MobilePickingExchangeDocumentResponse> Documents);

public sealed record MobilePickingResolveSourceRequest(string Barcode);
public sealed record MobilePickingCreateRequest(
    Guid ClientRequestId, Guid WarehouseId, string Barcode);
public sealed record MobilePickingAttachSourceRequest(
    Guid ClientRequestId, Guid WarehouseId, string Barcode);
public sealed record MobilePickingCommandRequest(
    Guid ClientRequestId, Guid WarehouseId);
public sealed record MobilePickingTakeOverRequest(
    Guid ClientRequestId, Guid WarehouseId, long ExpectedRevision);

public sealed record MobilePickingResourceIdResponse(Guid Id);

public sealed record MobilePickingWorkSkuResponse(
    Guid SkuId, string? Code, string? Name, string? Unit,
    bool TracksExpirationDate, decimal PlannedQuantity,
    decimal ConfirmedQuantity);

public sealed record MobilePickingWorkEntryResponse(
    Guid Id, Guid StorageLocationId, string LocationCode,
    Guid SkuId, DateOnly? ExpirationDate, decimal Quantity,
    string ConfirmedBy, DateTimeOffset ConfirmedAtUtc,
    bool FromReserve);

public sealed record MobilePickingWorkResponse(
    Guid Id, MobilePickingAssemblyStatus Status, long Revision,
    string ResponsibleUserId,
    IReadOnlyList<MobilePickingWorkSkuResponse> Skus,
    IReadOnlyList<MobilePickingWorkEntryResponse> Entries);

public sealed record MobilePickingRouteStepResponse(
    Guid StorageLocationId, string LocationCode, Guid SkuId,
    DateOnly? ExpirationDate, decimal Quantity, bool DirectPalletPick);

public sealed record MobilePickingRouteGapResponse(
    Guid SkuId, DateOnly? ExpirationDate, decimal Quantity);

public sealed record MobilePickingRouteResponse(
    long Revision, IReadOnlyList<MobilePickingRouteStepResponse> Steps,
    IReadOnlyList<MobilePickingRouteGapResponse> Gaps);

public sealed record MobilePickingAvailabilityResponse(
    Guid StorageLocationId, string LocationCode, DateOnly? ExpirationDate,
    decimal AccountingQuantity, decimal AvailableQuantity,
    decimal ReservedQuantity, decimal BlockedQuantity, bool Held);

public sealed record MobilePickingSkuResponse(
    Guid SkuId, decimal PlannedQuantity, decimal ConfirmedQuantity,
    decimal RemainingQuantity,
    IReadOnlyList<MobilePickingAvailabilityResponse> Locations);

public sealed record MobilePickingScanSourceRequest(
    Guid WarehouseId, Guid SkuId, string LocationCode,
    Guid? ScannedSkuId = null, DateOnly? ExpirationDate = null,
    bool DateGroupSelected = false);

public sealed record MobilePickingScannedSourceResponse(
    Guid StorageLocationId, string LocationCode, Guid SkuId,
    DateOnly? ExpirationDate, decimal AccountingQuantity,
    decimal AvailableQuantity, decimal SuggestedQuantity,
    decimal ReservedQuantity, decimal BlockedQuantity,
    bool RequiresVisualExpiryCheck);

public sealed record MobilePickingHoldPalletRequest(
    Guid ClientRequestId, Guid WarehouseId, long ExpectedRevision,
    string LocationCode, Guid SkuId);

public sealed record MobilePickingReturnPalletRequest(
    Guid ClientRequestId, Guid WarehouseId, long ExpectedRevision,
    string ScannedLocationCode);

public sealed record MobilePickingConfirmRequest(
    Guid ClientRequestId, Guid WarehouseId, long ExpectedRevision,
    string ScannedLocationCode, Guid SkuId, DateOnly? ExpirationDate,
    decimal Quantity, Guid? ScannedSkuId = null);

public sealed record MobilePickingReduceRequest(
    Guid ClientRequestId, Guid WarehouseId, long ExpectedRevision,
    string ScannedLocationCode, decimal Quantity,
    bool PhysicalReturnConfirmed, string? Explanation = null);

public sealed record MobilePickingShortageRequest(
    Guid ClientRequestId, Guid WarehouseId, long ExpectedRevision,
    string ScannedLocationCode, Guid SkuId, DateOnly? ExpirationDate,
    decimal ExpectedQuantity, decimal FoundQuantity,
    Guid? ExistingPickingEntryId = null, string? Comment = null);

public sealed record MobilePickingClosureCheckResponse(
    long Revision, bool FactMatchesPlan, bool CanOperatorClose,
    IReadOnlyList<string> HardBlocks, IReadOnlyList<string> ManagerIssues);

public sealed record MobilePickingCloseRequest(
    Guid ClientRequestId, Guid WarehouseId, long ExpectedRevision);
