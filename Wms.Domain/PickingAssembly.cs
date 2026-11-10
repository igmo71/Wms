using System.Globalization;
using Wms.Common;
using Wms.Domain.Enums;

namespace Wms.Domain;

public sealed class PickingAssembly
{
    private readonly List<PickingAssemblySourceLink> _sourceLinks = [];
    private readonly List<PickingAssemblyPlanLine> _planLines = [];

    private PickingAssembly() { }

    public Guid Id { get; private set; }
    public string Number { get; private set; } = null!;
    public Guid WarehouseId { get; private set; }
    public Warehouse? Warehouse { get; private set; }
    public PickingAssemblyStatus Status { get; private set; }
    public long OperationalRevision { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public string ResponsibleUserId { get; private set; } = null!;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? FirstStartAttemptAtUtc { get; private set; }
    public DateTimeOffset? StartedAtUtc { get; private set; }
    public Guid? StartLeaseId { get; private set; }
    public DateTimeOffset? StartLeaseUntilUtc { get; private set; }
    public DateTimeOffset? ClosedAtUtc { get; private set; }
    public DateTimeOffset? CancelledAtUtc { get; private set; }
    public string? CancelledBy { get; private set; }
    public IReadOnlyCollection<PickingAssemblySourceLink> SourceLinks => _sourceLinks;
    public IReadOnlyCollection<PickingAssemblyPlanLine> PlanLines => _planLines;
    public IReadOnlyCollection<PickingAssemblySourceLink> ActiveSources =>
        _sourceLinks.Where(x => x.DetachedAtUtc is null).ToArray();

    public static OperationResult<PickingAssembly> CreateNew(
        Guid warehouseId, string userId, DateTimeOffset createdAtUtc)
    {
        if (warehouseId == Guid.Empty || !ValidUser(userId) || createdAtUtc == default)
            return OperationError.Invalid("Для сборки обязательны склад, исполнитель и время.");

        return new PickingAssembly
        {
            Id = Guid.NewGuid(), WarehouseId = warehouseId,
            CreatedBy = userId.Trim(), ResponsibleUserId = userId.Trim(),
            CreatedAtUtc = createdAtUtc, Status = PickingAssemblyStatus.Draft
        };
    }

    public OperationResult AssignNumber(string number)
    {
        if (!string.IsNullOrEmpty(Number))
            return OperationError.Conflict("Номер сборки уже назначен.");
        if (number is null || number.Length != 13
            || !DateTime.TryParseExact(number, "yyMMdd-HHmmss",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            return OperationError.Invalid("Номер сборки должен иметь формат yyMMdd-HHmmss.");
        Number = number;
        return OperationResult.Success();
    }

    public OperationResult<PickingAssemblySourceLink> AttachSource(
        Guid shippingOrderId, string sourceSnapshotJson,
        IReadOnlyDictionary<Guid, decimal> planBySku,
        string userId, DateTimeOffset attachedAtUtc)
    {
        if (Status != PickingAssemblyStatus.Draft)
            return OperationError.Conflict("Состав сборки зафиксирован после попытки начала Отбора.");
        if (_sourceLinks.Any(x => x.ShippingOrderId == shippingOrderId && x.DetachedAtUtc is null))
            return OperationError.Conflict("Документ 1С уже привязан к этой сборке.");
        var validation = ValidatePlan(planBySku);
        if (!validation.IsSuccess)
            return validation.Error!;
        if (attachedAtUtc < CreatedAtUtc)
            return OperationError.Invalid("Время привязки предшествует созданию сборки.");

        var link = PickingAssemblySourceLink.Create(Id, shippingOrderId,
            sourceSnapshotJson, userId, attachedAtUtc);
        if (!link.IsSuccess)
            return link.Error!;
        var lines = CreatePlanLines(shippingOrderId, planBySku);
        if (!lines.IsSuccess)
            return lines.Error!;

        _sourceLinks.Add(link.Value!);
        _planLines.AddRange(lines.Value!);
        OperationalRevision++;
        return link.Value!;
    }

    public OperationResult RefreshDraftSourcePlan(
        Guid shippingOrderId, string sourceSnapshotJson,
        IReadOnlyDictionary<Guid, decimal> planBySku)
    {
        if (Status != PickingAssemblyStatus.Draft)
            return OperationError.Conflict("После попытки начала Отбора источник не меняет план WMS.");
        var link = ActiveLink(shippingOrderId);
        if (link is null)
            return OperationError.NotFound("Документ 1С не привязан к сборке.");
        var validation = ValidatePlan(planBySku);
        if (!validation.IsSuccess)
            return validation.Error!;
        var refresh = link.RefreshPlanSource(sourceSnapshotJson);
        if (!refresh.IsSuccess)
            return refresh;

        var existing = _planLines.Where(x => x.ShippingOrderId == shippingOrderId)
            .ToDictionary(x => x.StockKeepingUnitId);
        foreach (var line in existing.Values)
        {
            if (planBySku.TryGetValue(line.StockKeepingUnitId, out var quantity))
                line.SetQuantity(quantity);
            else
                _planLines.Remove(line);
        }
        foreach (var (skuId, quantity) in planBySku)
        {
            if (!existing.ContainsKey(skuId))
                _planLines.Add(PickingAssemblyPlanLine.Create(Id,
                    shippingOrderId, skuId, quantity).Value!);
        }
        OperationalRevision++;
        return OperationResult.Success();
    }

    public OperationResult DetachSource(Guid shippingOrderId,
        string userId, DateTimeOffset detachedAtUtc)
    {
        if (Status != PickingAssemblyStatus.Draft)
            return OperationError.Conflict("После попытки начала Отбора документ открепить нельзя.");
        var link = ActiveLink(shippingOrderId);
        if (link is null)
            return OperationError.NotFound("Документ 1С не привязан к сборке.");
        var detach = link.Detach(userId, detachedAtUtc);
        if (!detach.IsSuccess)
            return detach;

        _planLines.RemoveAll(x => x.ShippingOrderId == shippingOrderId);
        if (_sourceLinks.All(x => x.DetachedAtUtc is not null))
        {
            Status = PickingAssemblyStatus.Cancelled;
            CancelledAtUtc = detachedAtUtc;
            CancelledBy = userId.Trim();
        }
        OperationalRevision++;
        return OperationResult.Success();
    }

    public OperationResult FreezeForStart(string userId, DateTimeOffset atUtc)
    {
        if (Status != PickingAssemblyStatus.Draft || ActiveSources.Count == 0)
            return OperationError.Conflict("Начать можно только непустую сборку в черновике.");
        if (ResponsibleUserId != userId?.Trim() || atUtc < CreatedAtUtc)
            return OperationError.Invalid("Начать сборку может её ответственный исполнитель.");
        Status = PickingAssemblyStatus.Preparing;
        FirstStartAttemptAtUtc = atUtc;
        OperationalRevision++;
        return OperationResult.Success();
    }

    public OperationResult RecordSourceStartResult(Guid leaseId, Guid shippingOrderId,
        DateTimeOffset atUtc, string? error)
    {
        if (Status != PickingAssemblyStatus.Preparing || StartLeaseId != leaseId)
            return OperationError.Conflict("Сборка не ожидает запуска источников.");
        var link = ActiveLink(shippingOrderId);
        if (link is null || link.StartConfirmedAtUtc is not null)
            return OperationError.Conflict("Источник отсутствует или его переход уже подтверждён.");
        if (FirstStartAttemptAtUtc is not DateTimeOffset startAtUtc
            || atUtc < startAtUtc || error is { Length: > 2000 })
            return OperationError.Invalid("Результат запуска источника недопустим.");
        link.RecordStartAttempt(atUtc, error);
        StartLeaseUntilUtc = atUtc.AddMinutes(10);
        OperationalRevision++;
        return OperationResult.Success();
    }

    public OperationResult RecordStartPatchIntent(Guid leaseId, Guid shippingOrderId,
        DateTimeOffset atUtc)
    {
        if (Status != PickingAssemblyStatus.Preparing || StartLeaseId != leaseId)
            return OperationError.Conflict("Сборка не ожидает этого запуска источника.");
        var link = ActiveLink(shippingOrderId);
        if (link is null || link.StartConfirmedAtUtc is not null || atUtc == default)
            return OperationError.Conflict("Источник отсутствует или уже подтверждён.");
        link.RecordStartPatchIntent(atUtc);
        StartLeaseUntilUtc = atUtc.AddMinutes(10);
        OperationalRevision++;
        return OperationResult.Success();
    }

    public OperationResult CompleteStart(Guid leaseId, DateTimeOffset atUtc)
    {
        if (Status != PickingAssemblyStatus.Preparing || StartLeaseId != leaseId
            || ActiveSources.Count == 0
            || ActiveSources.Any(x => x.StartConfirmedAtUtc is null))
            return OperationError.Conflict("Все документы 1С должны быть подготовлены к Отбору.");
        if (FirstStartAttemptAtUtc is not DateTimeOffset startAtUtc || atUtc < startAtUtc)
            return OperationError.Invalid("Время начала сборки недопустимо.");
        Status = PickingAssemblyStatus.InProgress;
        StartedAtUtc = atUtc;
        OperationalRevision++;
        return OperationResult.Success();
    }

    public OperationResult ClaimStart(Guid leaseId, DateTimeOffset atUtc,
        DateTimeOffset untilUtc)
    {
        if (Status != PickingAssemblyStatus.Preparing)
            return OperationError.Conflict("Сборка не ожидает запуска источников.");
        if (leaseId == Guid.Empty || atUtc == default || untilUtc <= atUtc)
            return OperationError.Invalid("Время или идентификатор запуска недопустимы.");
        if (StartLeaseId is not null && StartLeaseUntilUtc > atUtc)
            return OperationError.Conflict("Начало Отбора уже выполняется. Повторите после завершения попытки.");
        StartLeaseId = leaseId;
        StartLeaseUntilUtc = untilUtc;
        OperationalRevision++;
        return OperationResult.Success();
    }

    public OperationResult ReleaseStart(Guid leaseId)
    {
        if (StartLeaseId != leaseId)
            return OperationError.Conflict("Попытка начала Отбора уже заменена другой.");
        StartLeaseId = null;
        StartLeaseUntilUtc = null;
        OperationalRevision++;
        return OperationResult.Success();
    }

    public OperationResult TakeOver(string userId, long expectedRevision,
        DateTimeOffset atUtc)
    {
        if (Status is not (PickingAssemblyStatus.Preparing or PickingAssemblyStatus.InProgress))
            return OperationError.Conflict("Передать можно только начатую сборку.");
        if (OperationalRevision != expectedRevision)
            return OperationError.Conflict("Сборка изменилась. Обновите экран перед передачей.");
        if (StartLeaseId is not null && StartLeaseUntilUtc > atUtc)
            return OperationError.Conflict("Дождитесь завершения текущей попытки начала Отбора.");
        if (!ValidUser(userId) || atUtc == default || atUtc < CreatedAtUtc)
            return OperationError.Invalid("Исполнитель или время передачи недопустимы.");
        if (ResponsibleUserId == userId.Trim())
            return OperationError.Conflict("Сборка уже закреплена за вами.");

        ResponsibleUserId = userId.Trim();
        OperationalRevision++;
        return OperationResult.Success();
    }

    private PickingAssemblySourceLink? ActiveLink(Guid shippingOrderId) =>
        _sourceLinks.SingleOrDefault(x =>
            x.ShippingOrderId == shippingOrderId && x.DetachedAtUtc is null);

    private OperationResult<List<PickingAssemblyPlanLine>> CreatePlanLines(
        Guid shippingOrderId, IReadOnlyDictionary<Guid, decimal> planBySku)
    {
        var lines = new List<PickingAssemblyPlanLine>(planBySku.Count);
        foreach (var (skuId, quantity) in planBySku)
        {
            var line = PickingAssemblyPlanLine.Create(Id, shippingOrderId, skuId, quantity);
            if (!line.IsSuccess)
                return line.Error!;
            lines.Add(line.Value!);
        }
        return lines;
    }

    private static OperationResult ValidatePlan(IReadOnlyDictionary<Guid, decimal>? planBySku)
    {
        if (planBySku is null || planBySku.Any(x => x.Key == Guid.Empty
            || !WarehouseQuantity.IsNonNegative(x.Value)))
            return OperationError.Invalid("План содержит недопустимый SKU или количество.");
        return OperationResult.Success();
    }

    private static bool ValidUser(string? userId) =>
        !string.IsNullOrWhiteSpace(userId) && userId.Trim().Length <= 36;
}
