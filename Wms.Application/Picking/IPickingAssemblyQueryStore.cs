using Wms.Domain.Enums;

namespace Wms.Application.Picking;

public sealed record PickingAssemblyListSource(
    Guid SourceRefKey, string? Number, bool StartConfirmed,
    string? StartError, string? SourceCheckError);

public sealed record PickingAssemblyListItem(
    Guid Id, string Number, Guid WarehouseId, PickingAssemblyStatus Status,
    long Revision, string CreatedBy, string ResponsibleUserId,
    string ResponsibleDisplayName, DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    IReadOnlyList<PickingAssemblyListSource> Sources)
{
    public bool HasWarning => Status == PickingAssemblyStatus.Preparing
        || Sources.Any(x => x.StartError is not null || x.SourceCheckError is not null);
}

public sealed record PickingAssemblySourceView(
    Guid SourceRefKey, string? Number, ShippingOrderStatus Status,
    bool Posted, bool DeletionMark, DateTimeOffset? LastCheckedAtUtc,
    string? LastCheckError, DateTimeOffset? StartConfirmedAtUtc,
    DateTimeOffset? LastStartAttemptAtUtc, string? LastStartError,
    string PlanSourceSnapshotJson, string? CurrentSourceJson);

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
    IReadOnlyList<PickingAssemblyPlanLineView> PlanLines);

public interface IPickingAssemblyQueryStore
{
    Task<IReadOnlyList<PickingAssemblyListItem>> ListForOperatorAsync(
        Guid warehouseId, string userId, CancellationToken ct);
    Task<PickingAssemblyView?> GetAsync(Guid assemblyId, CancellationToken ct);
}
