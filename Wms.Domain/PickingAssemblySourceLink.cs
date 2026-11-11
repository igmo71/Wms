using System.Text.Json;
using Wms.Common;

namespace Wms.Domain;

public sealed class PickingAssemblySourceLink
{
    private PickingAssemblySourceLink() { }

    public Guid Id { get; private set; }
    public Guid PickingAssemblyId { get; private set; }
    public PickingAssembly? PickingAssembly { get; private set; }
    public Guid ShippingOrderId { get; private set; }
    public ShippingOrder? ShippingOrder { get; private set; }
    public string PlanSourceSnapshotJson { get; private set; } = null!;
    public DateTimeOffset AttachedAtUtc { get; private set; }
    public string AttachedBy { get; private set; } = null!;
    public DateTimeOffset? DetachedAtUtc { get; private set; }
    public string? DetachedBy { get; private set; }
    public DateTimeOffset? StartConfirmedAtUtc { get; private set; }
    public DateTimeOffset? StartPatchRequestedAtUtc { get; private set; }
    public DateTimeOffset? LastStartAttemptAtUtc { get; private set; }
    public string? LastStartError { get; private set; }

    internal static OperationResult<PickingAssemblySourceLink> Create(
        Guid assemblyId, Guid shippingOrderId, string sourceSnapshotJson,
        string userId, DateTimeOffset attachedAtUtc)
    {
        if (assemblyId == Guid.Empty || shippingOrderId == Guid.Empty
            || !ValidSnapshot(sourceSnapshotJson)
            || string.IsNullOrWhiteSpace(userId) || userId.Trim().Length > 36
            || attachedAtUtc == default)
            return OperationError.Invalid("Для связи нужны сборка, документ 1С, снимок, исполнитель и время.");

        return new PickingAssemblySourceLink
        {
            Id = Guid.NewGuid(), PickingAssemblyId = assemblyId,
            ShippingOrderId = shippingOrderId,
            PlanSourceSnapshotJson = sourceSnapshotJson,
            AttachedBy = userId.Trim(), AttachedAtUtc = attachedAtUtc
        };
    }

    internal OperationResult RefreshPlanSource(string sourceSnapshotJson)
    {
        if (DetachedAtUtc is not null || LastStartAttemptAtUtc is not null)
            return OperationError.Conflict("После начала Отбора исходный план документа не обновляется.");
        if (!ValidSnapshot(sourceSnapshotJson))
            return OperationError.Invalid("Снимок документа 1С должен быть JSON-объектом.");
        PlanSourceSnapshotJson = sourceSnapshotJson;
        return OperationResult.Success();
    }

    internal OperationResult Detach(string userId, DateTimeOffset detachedAtUtc)
    {
        if (DetachedAtUtc is not null || LastStartAttemptAtUtc is not null)
            return OperationError.Conflict("Документ уже откреплён или Отбор начат.");
        if (string.IsNullOrWhiteSpace(userId) || userId.Trim().Length > 36
            || detachedAtUtc < AttachedAtUtc)
            return OperationError.Invalid("Исполнитель или время открепления недопустимы.");
        DetachedAtUtc = detachedAtUtc;
        DetachedBy = userId.Trim();
        return OperationResult.Success();
    }

    internal void ReleaseAfterCancellation(string userId, DateTimeOffset releasedAtUtc)
    {
        DetachedAtUtc = releasedAtUtc;
        DetachedBy = userId.Trim();
    }

    internal void RecordStartAttempt(DateTimeOffset atUtc, string? error)
    {
        LastStartAttemptAtUtc = atUtc;
        LastStartError = error;
        if (error is null)
            StartConfirmedAtUtc = atUtc;
    }

    internal void RecordStartPatchIntent(DateTimeOffset atUtc)
    {
        StartPatchRequestedAtUtc ??= atUtc;
    }

    private static bool ValidSnapshot(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        try
        {
            using var json = JsonDocument.Parse(value);
            return json.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
