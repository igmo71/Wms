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

public sealed record MobilePickingListSourceResponse(
    Guid SourceRefKey, string? Number, bool StartConfirmed,
    string? StartError, string? SourceCheckError);

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
    string PlanSourceSnapshotJson, string? CurrentSourceJson);

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

public sealed record MobilePickingResolveSourceRequest(string Barcode);
public sealed record MobilePickingCreateRequest(
    Guid ClientRequestId, Guid WarehouseId, string Barcode);
public sealed record MobilePickingAttachSourceRequest(
    Guid ClientRequestId, Guid WarehouseId, string Barcode);
public sealed record MobilePickingCommandRequest(
    Guid ClientRequestId, Guid WarehouseId);
public sealed record MobilePickingTakeOverRequest(
    Guid ClientRequestId, Guid WarehouseId, long ExpectedRevision);
