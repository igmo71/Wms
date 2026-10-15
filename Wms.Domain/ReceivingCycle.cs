using Wms.Common;

namespace Wms.Domain;

public sealed class ReceivingCycle
{
    private readonly List<ReceivingCycleSourceVersion> _sourceVersions = [];

    private ReceivingCycle() { }

    public Guid Id { get; private set; }
    public Guid ReceivingOrderId { get; private set; }
    public ReceivingOrder? ReceivingOrder { get; private set; }
    public int Number { get; private set; }
    public Guid? ReceivingLocationId { get; private set; }
    public StorageLocation? ReceivingLocation { get; private set; }
    public DateTimeOffset? StartedAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public string? CompletedBy { get; private set; }
    public string? PlanSnapshotJson { get; private set; }
    public string? FactSnapshotJson { get; private set; }
    public bool SourceCheckFailed { get; private set; }
    public IReadOnlyCollection<ReceivingCycleSourceVersion> SourceVersions => _sourceVersions;

    internal static ReceivingCycle Create(Guid orderId, int number) => new()
    {
        Id = Guid.NewGuid(), ReceivingOrderId = orderId, Number = number
    };

    public OperationResult Start(Guid receivingLocationId, DateTimeOffset startedAtUtc)
    {
        if (ReceivingLocationId is not null || CompletedAtUtc is not null)
            return OperationError.Conflict("Цикл приёмки уже начат или завершён.");
        if (receivingLocationId == Guid.Empty || startedAtUtc == default)
            return OperationError.Invalid("Место и время начала приёмки обязательны.");

        ReceivingLocationId = receivingLocationId;
        StartedAtUtc = startedAtUtc;
        return OperationResult.Success();
    }

    public OperationResult Complete(DateTimeOffset completedAtUtc, string userId,
        string planSnapshotJson, string factSnapshotJson, bool sourceCheckFailed,
        IReadOnlyCollection<(Guid SourceId, Guid VersionId)> sourceVersions)
    {
        if (CompletedAtUtc is not null)
            return OperationError.Conflict("Цикл приёмки уже завершён.");
        if (StartedAtUtc is null || completedAtUtc < StartedAtUtc
            || string.IsNullOrWhiteSpace(userId)
            || string.IsNullOrWhiteSpace(planSnapshotJson)
            || string.IsNullOrWhiteSpace(factSnapshotJson)
            || sourceVersions is null
            || sourceVersions.Any(x => x.SourceId == Guid.Empty || x.VersionId == Guid.Empty)
            || sourceVersions.Select(x => x.SourceId).Distinct().Count() != sourceVersions.Count)
            return OperationError.Invalid("Для завершения цикла нужны исполнитель и снимок решения.");

        CompletedAtUtc = completedAtUtc;
        CompletedBy = userId.Trim();
        PlanSnapshotJson = planSnapshotJson;
        FactSnapshotJson = factSnapshotJson;
        SourceCheckFailed = sourceCheckFailed;
        foreach (var (sourceId, versionId) in sourceVersions)
            _sourceVersions.Add(ReceivingCycleSourceVersion.Create(Id, sourceId, versionId));
        return OperationResult.Success();
    }
}

public sealed class ReceivingCycleSourceVersion
{
    private ReceivingCycleSourceVersion() { }

    public Guid ReceivingCycleId { get; private set; }
    public ReceivingCycle? ReceivingCycle { get; private set; }
    public Guid ReceivingSourceDocumentId { get; private set; }
    public ReceivingSourceDocument? ReceivingSourceDocument { get; private set; }
    public Guid ReceivingSourceVersionId { get; private set; }
    public ReceivingSourceVersion? ReceivingSourceVersion { get; private set; }

    internal static ReceivingCycleSourceVersion Create(Guid cycleId, Guid sourceId, Guid versionId) => new()
    {
        ReceivingCycleId = cycleId,
        ReceivingSourceDocumentId = sourceId,
        ReceivingSourceVersionId = versionId
    };
}
