using Wms.Common;
using Wms.Domain.Enums;

namespace Wms.Domain;

public class ReceivingOrder
{
    private readonly List<ReceivingOrderItem> _items = [];
    private readonly List<ReceivingOrderParticipant> _participants = [];
    private readonly List<LicensePlateNumber> _licensePlateNumbers = [];
    private readonly List<ReceivingOrderSourceLink> _sourceLinks = [];
    private readonly List<ReceivingCycle> _cycles = [];

    private ReceivingOrder()
    {
    }

    public Guid Id { get; private set; }
    public int? ActiveCycleNumber { get; private set; }
    public long OperationalRevision { get; private set; }
    public bool DeletionMark { get; private set; }
    public bool Posted { get; private set; }
    public string? Number { get; private set; }
    public DateTime Date { get; private set; }
    public Guid WarehouseId { get; private set; }
    public Warehouse? Warehouse { get; private set; }
    public Guid? ReceivingLocationId { get; private set; }
    public StorageLocation? ReceivingLocation { get; private set; }
    public string? Comment { get; private set; }
    public ReceivingOrderStatus Status { get; private set; }
    public ReceivingOrderQueue Queue { get; private set; }
    public WarehouseOperation WarehouseOperation { get; private set; }
    public BusinessOperation BusinessOperation { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }
    public DateTimeOffset? StartedAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public string? StartedBy { get; private set; }
    public string? CompletedBy { get; private set; }
    public OrderSynchronizationLevel SynchronizationLevel { get; private set; }
    public DateTimeOffset? SynchronizationDetectedAtUtc { get; private set; }
    public string? SynchronizationFingerprint { get; private set; }
    public DateTimeOffset? SynchronizationAcknowledgedAtUtc { get; private set; }
    public string? SynchronizationAcknowledgedBy { get; private set; }
    public DateTimeOffset? CompletionSourceCheckedAtUtc { get; private set; }
    public string? CompletionFingerprint { get; private set; }
    public string? CompletionOriginalPlanJson { get; private set; }
    public string? CompletionCurrentPlanJson { get; private set; }
    public string? CompletionFactJson { get; private set; }
    public string? CompletionDifferencesJson { get; private set; }
    public DateTimeOffset? CompletionApprovedAtUtc { get; private set; }
    public string? CompletionApprovedBy { get; private set; }
    public string? CompletionApprovalReason { get; private set; }
    public Guid ShipperId { get; private set; }
    public PartyType ShipperType { get; private set; }
    public PartyInfo? Shipper { get; private set; }
    public Guid BaseOrderId { get; private set; }
    public string? BaseOrderType { get; private set; }
    public IReadOnlyCollection<ReceivingOrderItem> Items => _items;
    public IReadOnlyCollection<ReceivingOrderParticipant> Participants => _participants;
    public IReadOnlyCollection<LicensePlateNumber> LicensePlateNumbers => _licensePlateNumbers;
    public IReadOnlyCollection<ReceivingOrderSourceLink> SourceLinks => _sourceLinks;
    public IReadOnlyCollection<ReceivingCycle> Cycles => _cycles;

    public void RecordSourceCompositionChange() => AdvanceOperationalRevision();

    public OperationResult<ReceivingCycle> CompleteCurrentCycle(
        string userId, DateTimeOffset completedAtUtc, bool canManage,
        string planSnapshotJson, string factSnapshotJson, bool sourceCheckFailed,
        IReadOnlyCollection<(Guid SourceId, Guid VersionId)> sourceVersions)
    {
        if (Status != ReceivingOrderStatus.InReceiving || ActiveCycleNumber is not int number)
            return OperationError.Conflict("Ордер не находится в активном цикле приёмки.");
        var cycle = _cycles.SingleOrDefault(x => x.Number == number);
        if (cycle is null || cycle.CompletedAtUtc is not null || cycle.StartedAtUtc is null)
            return OperationError.Conflict("Активный цикл приёмки не найден.");
        if (!canManage && !_participants.Any(x =>
                x.ReceivingCycleId == cycle.Id && x.UserId == userId?.Trim()))
            return OperationError.Conflict("Завершить приёмку может участник текущего цикла.");
        if (_licensePlateNumbers.Any(x => x.Status == LicensePlateNumberStatus.Open))
            return OperationError.Conflict("Перед завершением закройте или отмените открытые LPN.");

        var completed = cycle.Complete(completedAtUtc, userId,
            planSnapshotJson, factSnapshotJson, sourceCheckFailed, sourceVersions);
        if (!completed.IsSuccess)
            return completed.Error!;
        Status = ReceivingOrderStatus.Received;
        CompletedAtUtc = completedAtUtc;
        CompletedBy = userId.Trim();
        AdvanceOperationalRevision();
        return cycle;
    }

    public OperationResult<ReceivingCycle> ReopenForNextCycle(
        DateTimeOffset reopenedAtUtc)
    {
        if (Status != ReceivingOrderStatus.Received || ActiveCycleNumber is not int number
            || number == int.MaxValue)
            return OperationError.Conflict("Повторно открыть можно только завершённый ордер.");
        var previous = _cycles.SingleOrDefault(x => x.Number == number);
        if (previous?.CompletedAtUtc is null || reopenedAtUtc < previous.CompletedAtUtc
            || _cycles.Any(x => x.Number == number + 1))
            return OperationError.Conflict("Завершённый цикл изменился; обновите ордер.");

        var next = ReceivingCycle.Create(Id, number + 1);
        _cycles.Add(next);
        ActiveCycleNumber = next.Number;
        Status = ReceivingOrderStatus.ReadyForReceiving;
        ReceivingLocationId = null;
        StartedAtUtc = null;
        StartedBy = null;
        CompletedAtUtc = null;
        CompletedBy = null;
        AdvanceOperationalRevision();
        return next;
    }

    public OperationResult<IReadOnlyList<ReceivingOrderItem>> ApplyCurrentSourcePlan(
        IReadOnlyDictionary<Guid, decimal?> planBySku)
    {
        if (planBySku is null || planBySku.Any(x => x.Key == Guid.Empty
            || x.Value is decimal quantity && !WarehouseQuantity.IsNonNegative(quantity)))
            return OperationError.Invalid("Текущий план источников содержит недопустимое количество.");

        var changed = false;
        foreach (var item in _items)
        {
            var quantity = planBySku.GetValueOrDefault(item.StockKeepingUnitId, 0m);
            if (item.CurrentPlanQuantity == quantity)
                continue;
            var update = item.SetCurrentPlan(quantity);
            if (!update.IsSuccess)
                return update.Error!;
            changed = true;
        }

        var added = new List<ReceivingOrderItem>();
        foreach (var (skuId, quantity) in planBySku.Where(x =>
            _items.All(item => item.StockKeepingUnitId != x.Key)))
        {
            var item = ReceivingOrderItem.CreateForSku(Id, skuId, quantity);
            if (!item.IsSuccess)
                return item.Error!;
            _items.Add(item.Value!);
            added.Add(item.Value!);
            changed = true;
        }
        if (changed)
            AdvanceOperationalRevision();
        return added;
    }

    public static OperationResult<ReceivingOrder> CreateNew(Guid warehouseId, DateTimeOffset createdAtUtc)
    {
        if (warehouseId == Guid.Empty || createdAtUtc == default)
            return OperationError.Invalid("Склад и время создания приходного ордера обязательны.");

        var order = new ReceivingOrder
        {
            Id = Guid.NewGuid(),
            WarehouseId = warehouseId,
            CreatedAtUtc = createdAtUtc,
            Status = ReceivingOrderStatus.ReadyForReceiving,
            ActiveCycleNumber = 1
        };
        order._cycles.Add(ReceivingCycle.Create(order.Id, 1));
        return order;
    }

    public OperationResult<ReceivingJoinOutcome> JoinCurrentCycle(
        Guid? receivingLocationId, string userId, DateTimeOffset joinedAtUtc)
    {
        if (Status is not (ReceivingOrderStatus.ReadyForReceiving or ReceivingOrderStatus.InReceiving)
            || ActiveCycleNumber is not int number)
            return OperationError.Conflict("У ордера нет действующего цикла приёмки.");
        if (string.IsNullOrWhiteSpace(userId) || joinedAtUtc == default)
            return OperationError.Invalid("Для вступления нужны пользователь и время.");

        var cycle = _cycles.SingleOrDefault(x => x.Number == number);
        if (cycle is null || cycle.CompletedAtUtc is not null)
            return OperationError.Conflict("Действующий цикл приёмки не найден.");

        var started = Status == ReceivingOrderStatus.ReadyForReceiving;
        if (started)
        {
            if (receivingLocationId is not Guid locationId || locationId == Guid.Empty)
                return OperationError.Invalid("Первый участник должен выбрать место приёмки.");
            if (joinedAtUtc < CreatedAtUtc)
                return OperationError.Invalid("Время вступления предшествует созданию ордера.");
            var start = cycle.Start(locationId, joinedAtUtc);
            if (!start.IsSuccess)
                return start.Error!;
            ReceivingLocationId = locationId;
            Status = ReceivingOrderStatus.InReceiving;
            StartedAtUtc = joinedAtUtc;
            StartedBy = userId.Trim();
        }
        else if (cycle.ReceivingLocationId is not Guid currentLocationId
            || receivingLocationId is Guid selectedLocationId && selectedLocationId != currentLocationId)
            return OperationError.Conflict("Место приёмки текущего цикла изменилось.");

        var location = cycle.ReceivingLocationId!.Value;
        if (_participants.Any(x => x.ReceivingCycleId == cycle.Id && x.UserId == userId.Trim()))
            return new ReceivingJoinOutcome(cycle.Id, location, started, false);

        var participant = ReceivingOrderParticipant.CreateForCycle(cycle, userId, joinedAtUtc);
        if (!participant.IsSuccess)
            return participant.Error!;
        _participants.Add(participant.Value!);
        AdvanceOperationalRevision();
        return new ReceivingJoinOutcome(cycle.Id, location, started, true);
    }

    public OperationResult<bool> OpenReceivingLpn(
        LicensePlateNumber lpn, string userId, DateTimeOffset createdAtUtc)
    {
        var cycle = ActiveReceivingCycle();
        if (cycle is null)
            return OperationError.Conflict("Ордер не находится в действующем цикле приёмки.");
        if (!HasCurrentParticipant(userId, cycle.Id))
            return OperationError.Conflict("Сначала присоединитесь к текущему циклу приёмки.");
        if (lpn.Status != LicensePlateNumberStatus.Available)
            return lpn.ReceivingOrderId == Id
                ? false
                : OperationError.Conflict("Этикетка уже относится к другому ордеру.");

        var assigned = lpn.AssignReceiving(cycle, userId, createdAtUtc);
        if (!assigned.IsSuccess)
            return assigned.Error!;
        if (!_licensePlateNumbers.Contains(lpn))
            _licensePlateNumbers.Add(lpn);
        AdvanceOperationalRevision();
        return true;
    }

    public OperationResult EditReceivingLpn(
        LicensePlateNumber lpn, Guid skuId, DateOnly? expirationDate, decimal quantity,
        string userId, DateTimeOffset occurredAtUtc, bool canManage)
    {
        var access = ValidateCurrentLpn(lpn, userId, canManage);
        if (!access.IsSuccess)
            return access;
        if (_items.All(x => x.StockKeepingUnitId != skuId))
            return OperationError.NotFound("SKU отсутствует в составе ордера.");
        var changed = lpn.SetReceivingItem(
            skuId, expirationDate, quantity, userId, occurredAtUtc, canManage);
        if (changed.IsSuccess)
            AdvanceOperationalRevision();
        return changed;
    }

    public OperationResult CancelReceivingLpn(
        LicensePlateNumber lpn, string userId, DateTimeOffset occurredAtUtc, bool canManage)
    {
        var access = ValidateCurrentLpn(lpn, userId, canManage);
        if (!access.IsSuccess)
            return access;
        var cancelled = lpn.CancelOpen(userId, occurredAtUtc, canManage);
        if (cancelled.IsSuccess)
            AdvanceOperationalRevision();
        return cancelled;
    }

    public OperationResult<decimal> CloseReceivingLpn(
        LicensePlateNumber lpn, string userId, DateTimeOffset occurredAtUtc, bool canManage)
    {
        var access = ValidateCurrentLpn(lpn, userId, canManage);
        if (!access.IsSuccess)
            return access.Error!;

        var active = lpn.Items.Where(x => !x.IsDeleted).Take(2).ToArray();
        if (active.Length != 1 || !WarehouseQuantity.IsPositive(active[0].Quantity))
            return OperationError.Invalid("Закрыть можно только положительную монопалету.");
        var orderItem = _items.SingleOrDefault(x =>
            x.StockKeepingUnitId == active[0].StockKeepingUnitId);
        if (orderItem is null)
            return OperationError.NotFound("SKU отсутствует в составе ордера.");

        decimal fact;
        try
        {
            fact = checked(_licensePlateNumbers
                .Where(x => x.Status == LicensePlateNumberStatus.Closed)
                .SelectMany(x => x.Items)
                .Where(x => !x.IsDeleted && x.StockKeepingUnitId == active[0].StockKeepingUnitId)
                .Sum(x => x.Quantity) + active[0].Quantity);
        }
        catch (OverflowException)
        {
            return OperationError.Invalid("Суммарный факт SKU выходит за допустимый диапазон.");
        }
        if (!WarehouseQuantity.IsNonNegative(fact))
            return OperationError.Invalid("Суммарный факт SKU выходит за допустимый диапазон.");

        var closed = lpn.CloseReceiving(userId, occurredAtUtc, canManage);
        if (!closed.IsSuccess)
            return closed.Error!;
        orderItem.SetAggregatedLpnFact(fact);
        AdvanceOperationalRevision();
        return fact;
    }

    public OperationResult<decimal> CorrectClosedReceivingLpnQuantity(
        LicensePlateNumber lpn, decimal quantity, string userId, DateTimeOffset occurredAtUtc)
    {
        if (lpn is null || lpn.ReceivingOrderId != Id || !_licensePlateNumbers.Contains(lpn))
            return OperationError.NotFound("LPN не найдена в приходном ордере.");
        if (!WarehouseQuantity.IsNonNegative(quantity))
            return OperationError.Invalid("Новое количество LPN должно быть неотрицательным.");

        var active = lpn.Items.Where(x => !x.IsDeleted).Take(2).ToArray();
        if (active.Length != 1)
            return OperationError.Conflict("Приёмная LPN должна содержать один действующий SKU.");
        var orderItem = _items.SingleOrDefault(x =>
            x.StockKeepingUnitId == active[0].StockKeepingUnitId);
        if (orderItem is null)
            return OperationError.NotFound("SKU отсутствует в составе ордера.");

        decimal fact;
        try
        {
            fact = checked(_licensePlateNumbers
                .Where(x => x.Status == LicensePlateNumberStatus.Closed && x.Id != lpn.Id)
                .SelectMany(x => x.Items)
                .Where(x => !x.IsDeleted && x.StockKeepingUnitId == active[0].StockKeepingUnitId)
                .Sum(x => x.Quantity) + quantity);
        }
        catch (OverflowException)
        {
            return OperationError.Invalid("Суммарный факт SKU выходит за допустимый диапазон.");
        }
        if (!WarehouseQuantity.IsNonNegative(fact))
            return OperationError.Invalid("Суммарный факт SKU выходит за допустимый диапазон.");

        var changed = lpn.CorrectClosedReceivingQuantity(quantity, userId, occurredAtUtc);
        if (!changed.IsSuccess)
            return changed.Error!;
        orderItem.SetAggregatedLpnFact(fact);
        AdvanceOperationalRevision();
        return fact;
    }

    public OperationResult<decimal?> DeleteClosedReceivingLpn(
        LicensePlateNumber lpn, bool zeroPreviouslyConfirmed,
        string userId, DateTimeOffset occurredAtUtc)
    {
        if (lpn is null || lpn.ReceivingOrderId != Id || !_licensePlateNumbers.Contains(lpn))
            return OperationError.NotFound("LPN не найдена в приходном ордере.");
        var active = lpn.Items.Where(x => !x.IsDeleted).Take(2).ToArray();
        if (active.Length != 1)
            return OperationError.Conflict("Приёмная LPN должна содержать один действующий SKU.");
        var orderItem = _items.SingleOrDefault(x =>
            x.StockKeepingUnitId == active[0].StockKeepingUnitId);
        if (orderItem is null)
            return OperationError.NotFound("SKU отсутствует в составе ордера.");

        var remaining = _licensePlateNumbers
            .Where(x => x.Status == LicensePlateNumberStatus.Closed && x.Id != lpn.Id)
            .SelectMany(x => x.Items)
            .Where(x => !x.IsDeleted && x.StockKeepingUnitId == active[0].StockKeepingUnitId)
            .ToArray();
        decimal? fact;
        try
        {
            fact = remaining.Length > 0 ? checked(remaining.Sum(x => x.Quantity))
                : zeroPreviouslyConfirmed ? 0m : null;
        }
        catch (OverflowException)
        {
            return OperationError.Invalid("Суммарный факт SKU выходит за допустимый диапазон.");
        }
        if (fact is decimal quantity && !WarehouseQuantity.IsNonNegative(quantity))
            return OperationError.Invalid("Суммарный факт SKU выходит за допустимый диапазон.");

        var deleted = lpn.DeleteClosedReceiving(userId, occurredAtUtc);
        if (!deleted.IsSuccess)
            return deleted.Error!;
        orderItem.SetAggregatedLpnFact(fact);
        AdvanceOperationalRevision();
        return fact;
    }

    public OperationResult<bool> ConfirmZeroReceived(Guid skuId, string userId)
    {
        var cycle = ActiveReceivingCycle();
        if (cycle is null || !HasCurrentParticipant(userId, cycle.Id))
            return OperationError.Conflict("Подтверждать отсутствие может участник текущего цикла.");
        var item = _items.SingleOrDefault(x => x.StockKeepingUnitId == skuId);
        if (item is null)
            return OperationError.NotFound("SKU отсутствует в составе ордера.");
        if (_licensePlateNumbers.Any(lpn =>
                (lpn.Status is LicensePlateNumberStatus.Open or LicensePlateNumberStatus.Closed)
                && lpn.Items.Any(x => !x.IsDeleted && x.StockKeepingUnitId == skuId)))
            return OperationError.Conflict("Для SKU уже есть действующая LPN.");
        if (item.FactQuantity == 0m)
            return false;
        item.SetAggregatedLpnFact(0m);
        AdvanceOperationalRevision();
        return true;
    }

    private ReceivingCycle? ActiveReceivingCycle() =>
        Status == ReceivingOrderStatus.InReceiving && ActiveCycleNumber is int number
            ? _cycles.SingleOrDefault(x => x.Number == number && x.CompletedAtUtc == null
                && x.StartedAtUtc != null)
            : null;

    private bool HasCurrentParticipant(string userId, Guid cycleId) =>
        _participants.Any(x => x.ReceivingCycleId == cycleId && x.UserId == userId?.Trim());

    private OperationResult ValidateCurrentLpn(
        LicensePlateNumber lpn, string userId, bool canManage)
    {
        var cycle = ActiveReceivingCycle();
        if (cycle is null || lpn.ReceivingOrderId != Id || lpn.ReceivingCycleId != cycle.Id)
            return OperationError.Conflict("LPN не относится к текущему циклу приёмки.");
        return canManage || HasCurrentParticipant(userId, cycle.Id)
            ? OperationResult.Success()
            : OperationError.Conflict("Сначала присоединитесь к текущему циклу приёмки.");
    }

    public double KnownFactWeightKg => _items.Sum(x => x.FactWeightKg ?? 0);
    public bool IsFactWeightComplete => _items.All(x => x.FactQuantity is decimal factQuantity
        && (factQuantity == 0 || x.FactWeightKg.HasValue));
    public int UnconfirmedItemCount => _items.Count(x => !x.IsFactConfirmed);

    public static OperationResult<ReceivingOrder> Create(
        ReceivingOrderImportSnapshot snapshot,
        DateTimeOffset createdAtUtc)
    {
        var validationResult = ValidateInitialSourcePlan(snapshot, createdAtUtc);
        if (!validationResult.IsSuccess)
        {
            return validationResult.Error!;
        }

        if (snapshot.Status != ReceivingOrderStatus.ReadyForReceiving)
        {
            return OperationError.Invalid(
                "Приходный ордер можно создать только в статусе готовности к приёмке.");
        }

        var order = new ReceivingOrder
        {
            Id = snapshot.Id,
            CreatedAtUtc = createdAtUtc,
            Status = ReceivingOrderStatus.ReadyForReceiving,
            SynchronizationLevel = OrderSynchronizationLevel.Synchronized
        };

        order.ApplyImport(snapshot);
        foreach (var itemSnapshot in snapshot.Items)
        {
            var itemResult = ReceivingOrderItem.Create(order.Id, itemSnapshot);
            if (!itemResult.IsSuccess)
            {
                return itemResult.Error!;
            }

            order._items.Add(itemResult.Value!);
        }

        order.SynchronizationFingerprint =
            ReceivingOrderSynchronizationComparer.Compare(order, snapshot).Fingerprint;

        return order;
    }

    public OperationResult<ReceivingOrderReconciliation> Reconcile(
        ReceivingOrderImportSnapshot snapshot,
        DateTimeOffset updatedAtUtc)
    {
        if (snapshot.Id != Id)
        {
            return OperationError.Invalid(
                "Импортируемый приходный ордер должен соответствовать существующему ордеру.");
        }

        if (updatedAtUtc == default)
        {
            return OperationError.Invalid("Время сверки приходного ордера обязательно.");
        }

        if (updatedAtUtc < CreatedAtUtc)
        {
            return OperationError.Invalid(
                "Время сверки приходного ордера не может предшествовать времени его создания.");
        }

        OrderSynchronizationAssessment assessment =
            AssessSynchronization(snapshot, updatedAtUtc);
        bool sourceStateChanged = ApplySourceState(snapshot);

        if (CanApplyInitialSourceDocument(snapshot))
        {
            OperationResult validationResult = ValidateInitialSourcePlan(snapshot, updatedAtUtc);
            if (!validationResult.IsSuccess)
            {
                assessment = EnsureBlockingAssessment(assessment, validationResult.Error!.Message);
            }
            else if (assessment.Level != OrderSynchronizationLevel.Synchronized
                || SynchronizationFingerprint != assessment.Fingerprint)
            {
                OperationResult itemsResult = ReconcileItems(snapshot.Items);
                if (!itemsResult.IsSuccess)
                {
                    return itemsResult.Error!;
                }

                ApplyImport(snapshot);
                UpdatedAtUtc = updatedAtUtc;
                assessment = AssessSynchronization(snapshot, updatedAtUtc);
                ApplySynchronizationAssessment(assessment, updatedAtUtc);
                return ReceivingOrderReconciliation.Updated;
            }
        }

        bool synchronizationStateChanged = ApplySynchronizationAssessment(
            assessment,
            updatedAtUtc);

        if (assessment.Level != OrderSynchronizationLevel.Synchronized)
        {
            return ReceivingOrderReconciliation.DifferencesDetected;
        }

        return synchronizationStateChanged || sourceStateChanged
            ? ReceivingOrderReconciliation.Updated
            : ReceivingOrderReconciliation.Unchanged;
    }

    internal OrderSynchronizationAssessment AssessSynchronization(
        ReceivingOrderImportSnapshot snapshot,
        DateTimeOffset checkedAtUtc)
    {
        OrderSynchronizationAssessment assessment =
            ReceivingOrderSynchronizationComparer.Compare(this, snapshot);

        if (!CanApplyInitialSourceDocument(snapshot))
        {
            return assessment;
        }

        OperationResult validationResult = ValidateInitialSourcePlan(snapshot, checkedAtUtc);
        return validationResult.IsSuccess
            ? assessment
            : EnsureBlockingAssessment(assessment, validationResult.Error!.Message);
    }

    internal bool ApplySynchronizationAssessment(
        OrderSynchronizationAssessment assessment,
        DateTimeOffset checkedAtUtc)
    {
        DateTimeOffset? detectedAtUtc = assessment.Level == OrderSynchronizationLevel.Synchronized
            ? null
            : SynchronizationLevel == assessment.Level
                && SynchronizationFingerprint == assessment.Fingerprint
                    ? SynchronizationDetectedAtUtc
                    : checkedAtUtc;

        bool changed = SynchronizationLevel != assessment.Level
            || SynchronizationDetectedAtUtc != detectedAtUtc
            || SynchronizationFingerprint != assessment.Fingerprint;

        SynchronizationLevel = assessment.Level;
        SynchronizationDetectedAtUtc = detectedAtUtc;
        SynchronizationFingerprint = assessment.Fingerprint;

        if (changed)
        {
            AdvanceOperationalRevision();
        }

        return changed;
    }

    internal OperationResult AcknowledgeSynchronization(
        ReceivingOrderImportSnapshot snapshot,
        OrderSynchronizationAssessment assessment,
        DateTimeOffset acknowledgedAtUtc,
        string userId)
    {
        OperationResult auditResult = ValidateAudit(
            acknowledgedAtUtc,
            userId,
            "Пользователь, подтверждающий расхождения, обязателен.");
        if (!auditResult.IsSuccess)
        {
            return auditResult;
        }

        if (assessment.Level != OrderSynchronizationLevel.RequiresOperatorDecision)
        {
            return OperationError.Conflict(
                "Подтвердить можно только расхождения, требующие решения оператора.");
        }

        Number = snapshot.Number;
        Date = snapshot.Date;
        Comment = snapshot.Comment;
        Queue = snapshot.Queue;
        ShipperId = snapshot.ShipperId;
        ShipperType = snapshot.ShipperType;
        SynchronizationLevel = OrderSynchronizationLevel.Synchronized;
        SynchronizationDetectedAtUtc = null;
        SynchronizationFingerprint = assessment.Fingerprint;
        SynchronizationAcknowledgedAtUtc = acknowledgedAtUtc;
        SynchronizationAcknowledgedBy = userId;
        UpdatedAtUtc = acknowledgedAtUtc;
        AdvanceOperationalRevision();
        return OperationResult.Success();
    }

    public OperationResult SetReceivingLocation(Guid receivingLocationId)
    {
        if (receivingLocationId == Guid.Empty)
        {
            return OperationError.Invalid("Идентификатор позиции приёмки обязателен.");
        }

        if (Status is not (ReceivingOrderStatus.ReadyForReceiving
            or ReceivingOrderStatus.InReceiving))
        {
            return OperationError.Invalid(
                "Позицию приёмки можно изменить только до завершения приёмки ордера.");
        }

        ReceivingLocationId = receivingLocationId;
        AdvanceOperationalRevision();
        return OperationResult.Success();
    }

    private OperationResult ValidateToSetInReceiving()
    {
        if (Status != ReceivingOrderStatus.ReadyForReceiving)
        {
            return OperationError.Invalid(
                "Взять в работу можно только ордер, готовый к приёмке.");
        }

        if (ReceivingLocationId is null)
        {
            return OperationError.Invalid(
                "Перед началом приёмки необходимо указать позицию приёмки.");
        }

        return OperationResult.Success();
    }

    public OperationResult SetInReceiving(DateTimeOffset startedAtUtc, string startedBy)
    {
        var validationResult = ValidateToSetInReceiving();
        if (!validationResult.IsSuccess)
        {
            return validationResult;
        }

        var auditResult = ValidateAudit(startedAtUtc, startedBy, "Необходимо указать пользователя, начавшего операцию.");
        if (!auditResult.IsSuccess)
        {
            return auditResult;
        }

        if (startedAtUtc < CreatedAtUtc)
        {
            return OperationError.Invalid(
                "Время начала приёмки не может предшествовать созданию ордера.");
        }

        Status = ReceivingOrderStatus.InReceiving;
        StartedAtUtc = startedAtUtc;
        StartedBy = startedBy.Trim();
        AdvanceOperationalRevision();
        return OperationResult.Success();
    }

    public OperationResult JoinReceiving(DateTimeOffset joinedAtUtc, string userId)
    {
        if (Status != ReceivingOrderStatus.InReceiving)
            return OperationError.Invalid("Присоединиться можно только к ордеру в работе.");

        var eligibility = ValidateReceivingParticipationEligibility();
        if (!eligibility.IsSuccess)
            return eligibility;

        if (_participants.Any(x => x.UserId == userId))
            return OperationResult.Success();

        var participantResult = ReceivingOrderParticipant.Create(Id, userId, joinedAtUtc);
        if (!participantResult.IsSuccess)
            return participantResult.Error!;

        _participants.Add(participantResult.Value!);
        AdvanceOperationalRevision();
        return OperationResult.Success();
    }

    public OperationResult ValidateReceivingParticipationEligibility()
    {
        if (DeletionMark)
            return OperationError.Conflict("Приходный ордер помечен на удаление в 1С.");
        if (!Posted)
            return OperationError.Conflict("Приходный ордер не проведён в 1С.");
        var duplicateSku = _items
            .GroupBy(x => x.StockKeepingUnitId)
            .FirstOrDefault(x => x.Count() > 1);
        return duplicateSku is null
            ? OperationResult.Success()
            : OperationError.Conflict(
                $"SKU указан в нескольких строках: {duplicateSku.Key}.");
    }

    public OperationResult<Guid> OpenOrCreateLicensePlateNumber(
        LicensePlateNumber lpn,
        DateTimeOffset createdAtUtc,
        string userId)
    {
        if (Status != ReceivingOrderStatus.InReceiving)
            return OperationError.Invalid("LPN можно создать только для ордера в работе.");
        if (!_participants.Any(x => x.UserId == userId))
            return OperationError.Conflict("Сначала присоединитесь к приходному ордеру.");

        if (lpn.Status != LicensePlateNumberStatus.Available)
        {
            return lpn.ReceivingOrderId == Id
                ? lpn.Id
                : OperationError.Conflict(
                    $"LPN {lpn.Code} относится к другому приходному ордеру.");
        }

        var assignResult = lpn.Assign(Id, userId, createdAtUtc);
        if (!assignResult.IsSuccess)
            return assignResult.Error!;

        if (!_licensePlateNumbers.Contains(lpn))
            _licensePlateNumbers.Add(lpn);
        AdvanceOperationalRevision();
        return lpn.Id;
    }

    public OperationResult CancelEmptyLicensePlateNumber(
        LicensePlateNumber lpn,
        DateTimeOffset cancelledAtUtc,
        string userId)
    {
        if (lpn.ReceivingOrderId != Id)
            return OperationError.NotFound("LPN не найден в приходном ордере.");
        if (Status != ReceivingOrderStatus.InReceiving)
            return OperationError.Invalid("LPN нельзя отменить после завершения приёмки.");

        var result = lpn.CancelEmpty(userId, cancelledAtUtc);
        if (result.IsSuccess)
            AdvanceOperationalRevision();
        return result;
    }

    public OperationResult SetLicensePlateNumberItemQuantity(
        LicensePlateNumber lpn,
        Guid stockKeepingUnitId,
        decimal quantity,
        DateTimeOffset occurredAtUtc,
        string userId,
        bool canManage)
    {
        var validation = ValidateLicensePlateNumberMutation(lpn, stockKeepingUnitId);
        if (!validation.IsSuccess)
            return validation;
        var result = lpn.SetItemQuantity(
            stockKeepingUnitId, quantity, userId, occurredAtUtc, canManage);
        if (!result.IsSuccess)
            return result;

        var aggregateResult = RecalculateLpnFacts();
        if (!aggregateResult.IsSuccess)
            return aggregateResult;
        AdvanceOperationalRevision();
        return OperationResult.Success();
    }

    public OperationResult RemoveLicensePlateNumberItem(
        LicensePlateNumber lpn,
        Guid stockKeepingUnitId,
        DateTimeOffset occurredAtUtc,
        string userId,
        bool canManage)
    {
        var validation = ValidateLicensePlateNumberMutation(lpn, stockKeepingUnitId);
        if (!validation.IsSuccess)
            return validation;
        var result = lpn.RemoveItem(stockKeepingUnitId, userId, occurredAtUtc, canManage);
        if (!result.IsSuccess)
            return result;

        var aggregateResult = RecalculateLpnFacts();
        if (!aggregateResult.IsSuccess)
            return aggregateResult;
        AdvanceOperationalRevision();
        return OperationResult.Success();
    }

    public OperationResult CloseLicensePlateNumber(
        LicensePlateNumber lpn,
        DateTimeOffset occurredAtUtc,
        string userId,
        bool canManage)
    {
        var validation = ValidateLicensePlateNumberMutation(lpn);
        if (!validation.IsSuccess)
            return validation;
        var result = lpn.Close(userId, occurredAtUtc, canManage);
        if (result.IsSuccess)
            AdvanceOperationalRevision();
        return result;
    }

    public OperationResult ReopenLicensePlateNumber(
        LicensePlateNumber lpn,
        DateTimeOffset occurredAtUtc,
        string userId,
        bool canManage)
    {
        var validation = ValidateLicensePlateNumberMutation(lpn);
        if (!validation.IsSuccess)
            return validation;
        var result = lpn.Reopen(userId, occurredAtUtc, canManage);
        if (result.IsSuccess)
            AdvanceOperationalRevision();
        return result;
    }

    private OperationResult ValidateLicensePlateNumberMutation(
        LicensePlateNumber lpn,
        Guid? stockKeepingUnitId = null)
    {
        if (Status != ReceivingOrderStatus.InReceiving)
            return OperationError.Invalid("Изменять LPN можно только во время приёмки.");
        if (lpn.ReceivingOrderId != Id || !_licensePlateNumbers.Contains(lpn))
            return OperationError.NotFound("LPN не найден в приходном ордере.");
        if (stockKeepingUnitId is Guid skuId
            && _items.All(x => x.StockKeepingUnitId != skuId))
        {
            return OperationError.NotFound("SKU отсутствует в плане приходного ордера.");
        }
        return OperationResult.Success();
    }

    private OperationResult RecalculateLpnFacts()
    {
        var activeItems = _licensePlateNumbers
            .Where(x => x.Status is not (LicensePlateNumberStatus.Cancelled
                or LicensePlateNumberStatus.Deleted))
            .SelectMany(x => x.Items)
            .Where(x => !x.IsDeleted)
            .ToLookup(x => x.StockKeepingUnitId);

        foreach (var orderItem in _items)
        {
            var lpnItems = activeItems[orderItem.StockKeepingUnitId];
            decimal? aggregate = lpnItems.Any() ? lpnItems.Sum(x => x.Quantity) : null;
            if (aggregate is decimal quantity && !WarehouseQuantity.IsNonNegative(quantity))
                return OperationError.Invalid("Суммарное количество SKU в LPN выходит за допустимый диапазон.");
            orderItem.SetAggregatedLpnFact(aggregate);
        }
        return OperationResult.Success();
    }

    private OperationResult ValidateToSetReceived(string userId, bool isManagerDecision)
    {
        if (Status != ReceivingOrderStatus.InReceiving)
        {
            return OperationError.Invalid(
                "Завершить приёмку можно только для ордера в приёмке или обработке.");
        }

        if (ReceivingLocationId is null)
        {
            return OperationError.Invalid(
                "Перед завершением приёмки необходимо указать позицию приёмки.");
        }

        if (!isManagerDecision && !_participants.Any(x => x.UserId == userId))
        {
            return OperationError.Conflict(
                "Завершить приёмку может только участник приходного ордера.");
        }

        var openLpns = _licensePlateNumbers
            .Where(x => x.Status == LicensePlateNumberStatus.Open)
            .Select(x => string.IsNullOrWhiteSpace(x.Code) ? x.Id.ToString("D") : x.Code)
            .ToList();
        if (openLpns.Count > 0)
        {
            return OperationError.Conflict(
                $"Перед завершением закройте LPN: {string.Join(", ", openLpns)}.");
        }

        if (_items.Any(x => !x.IsFactConfirmed))
        {
            return OperationError.Invalid(
                "Перед завершением приёмки необходимо проверить фактическое количество каждой строки.");
        }

        return OperationResult.Success();
    }

    public OperationResult SetReceived(
        ReceivingCompletionReconciliation reconciliation,
        string originalPlanJson,
        string currentPlanJson,
        string factJson,
        string differencesJson,
        DateTimeOffset completedAtUtc,
        string completedBy,
        bool canApproveQuantityDifference,
        string? approvalReason)
    {
        var validationResult = ValidateToSetReceived(
            completedBy, reconciliation.RequiresManager && canApproveQuantityDifference);
        if (!validationResult.IsSuccess)
        {
            return validationResult;
        }

        var auditResult = ValidateAudit(completedAtUtc, completedBy, "Необходимо указать пользователя, завершившего операцию.");
        if (!auditResult.IsSuccess)
        {
            return auditResult;
        }

        if (completedAtUtc < CreatedAtUtc || completedAtUtc < StartedAtUtc)
        {
            return OperationError.Invalid(
                "Время завершения приёмки не может предшествовать предыдущим операциям ордера.");
        }

        if (reconciliation.IsBlocking)
            return OperationError.Conflict("Завершение заблокировано свежей сверкой с 1С.");
        if (string.IsNullOrWhiteSpace(reconciliation.Assessment.Fingerprint)
            || string.IsNullOrWhiteSpace(originalPlanJson)
            || string.IsNullOrWhiteSpace(currentPlanJson)
            || string.IsNullOrWhiteSpace(factJson)
            || string.IsNullOrWhiteSpace(differencesJson))
        {
            return OperationError.Invalid("Снимок завершения приходного ордера обязателен.");
        }
        if (reconciliation.RequiresManager
            && (!canApproveQuantityDifference || string.IsNullOrWhiteSpace(approvalReason)))
        {
            return OperationError.Conflict(
                "Количественное расхождение должен подтвердить Manager с указанием причины.");
        }
        if (reconciliation.RequiresManager && approvalReason!.Trim().Length > 1000)
            return OperationError.Invalid("Причина решения Manager не должна превышать 1000 символов.");

        Status = ReceivingOrderStatus.Received;
        CompletedAtUtc = completedAtUtc;
        CompletedBy = completedBy.Trim();
        CompletionSourceCheckedAtUtc = reconciliation.CheckedAtUtc;
        CompletionFingerprint = reconciliation.Assessment.Fingerprint;
        CompletionOriginalPlanJson = originalPlanJson;
        CompletionCurrentPlanJson = currentPlanJson;
        CompletionFactJson = factJson;
        CompletionDifferencesJson = differencesJson;
        CompletionApprovedAtUtc = reconciliation.RequiresManager ? completedAtUtc : null;
        CompletionApprovedBy = reconciliation.RequiresManager ? completedBy.Trim() : null;
        CompletionApprovalReason = reconciliation.RequiresManager ? approvalReason!.Trim() : null;
        SynchronizationLevel = OrderSynchronizationLevel.Synchronized;
        SynchronizationDetectedAtUtc = null;
        SynchronizationFingerprint = reconciliation.Assessment.Fingerprint;
        AdvanceOperationalRevision();
        return OperationResult.Success();
    }

    private void AdvanceOperationalRevision() => OperationalRevision++;

    internal void SetShipper(PartyInfo? shipper)
    {
        Shipper = shipper;
    }

    private static OperationResult ValidateImport(
        ReceivingOrderImportSnapshot snapshot,
        DateTimeOffset occurredAtUtc)
    {
        if (snapshot.Id == Guid.Empty || snapshot.WarehouseId == Guid.Empty)
        {
            return OperationError.Invalid(
                "Идентификаторы приходного ордера и склада обязательны.");
        }

        if (snapshot.Date == default)
        {
            return OperationError.Invalid("Дата приходного ордера обязательна.");
        }

        if (occurredAtUtc == default)
        {
            return OperationError.Invalid("Время импорта обязательно.");
        }

        if (snapshot.Items is null
            || snapshot.Items.GroupBy(x => x.LineNumber).Any(x => x.Count() > 1))
        {
            return OperationError.Invalid(
                "Номера строк приходного ордера не должны повторяться.");
        }

        foreach (var itemSnapshot in snapshot.Items)
        {
            var itemResult = ReceivingOrderItem.ValidateImport(snapshot.Id, itemSnapshot);
            if (!itemResult.IsSuccess)
            {
                return itemResult;
            }
        }

        return OperationResult.Success();
    }

    private static OperationResult ValidateInitialSourcePlan(
        ReceivingOrderImportSnapshot snapshot,
        DateTimeOffset occurredAtUtc)
    {
        OperationResult validationResult = ValidateImport(snapshot, occurredAtUtc);
        if (!validationResult.IsSuccess)
        {
            return validationResult;
        }

        return snapshot.Items.Any(x => x.Quantity != x.PlanQuantity)
            ? OperationError.Invalid(
                "В начальном приходном ордере количество и количество упаковок в строках 1С должны совпадать.")
            : OperationResult.Success();
    }

    private bool CanApplyInitialSourceDocument(ReceivingOrderImportSnapshot snapshot) =>
        Status == ReceivingOrderStatus.ReadyForReceiving
        && StartedAtUtc is null
        && !DeletionMark
        && !snapshot.DeletionMark
        && snapshot.Posted
        && snapshot.Status != ReceivingOrderStatus.Unknown;

    private static OrderSynchronizationAssessment EnsureBlockingAssessment(
        OrderSynchronizationAssessment assessment,
        string reason) =>
        assessment.Level == OrderSynchronizationLevel.Blocking
            ? assessment
            : new OrderSynchronizationAssessment(
                assessment.Fingerprint,
                [
                    .. assessment.Differences,
                    new OrderSynchronizationDifference(
                        "initialPlan",
                        "Начальный план",
                        "Допустимый план WMS",
                        reason,
                        OrderSynchronizationLevel.Blocking)
                ]);

    private OperationResult ReconcileItems(
        IReadOnlyCollection<ReceivingOrderItemImportSnapshot> snapshots)
    {
        var importedItems = snapshots.ToDictionary(x => x.LineNumber);
        _items.RemoveAll(existingItem => !importedItems.ContainsKey(existingItem.LineNumber));

        var existingItems = _items.ToDictionary(x => x.LineNumber);
        foreach (var snapshot in snapshots)
        {
            if (existingItems.TryGetValue(snapshot.LineNumber, out var existingItem))
            {
                var itemResult = existingItem.Reconcile(snapshot);
                if (!itemResult.IsSuccess)
                {
                    return itemResult;
                }
            }
            else
            {
                var itemResult = ReceivingOrderItem.Create(Id, snapshot);
                if (!itemResult.IsSuccess)
                {
                    return itemResult.Error!;
                }

                _items.Add(itemResult.Value!);
            }
        }

        return OperationResult.Success();
    }

    private void ApplyImport(ReceivingOrderImportSnapshot snapshot)
    {
        ApplySourceState(snapshot);
        Number = snapshot.Number;
        Date = snapshot.Date;
        WarehouseId = snapshot.WarehouseId;
        Comment = snapshot.Comment;
        Queue = snapshot.Queue;
        WarehouseOperation = snapshot.WarehouseOperation;
        BusinessOperation = snapshot.BusinessOperation;
        ShipperId = snapshot.ShipperId;
        ShipperType = snapshot.ShipperType;
        BaseOrderId = snapshot.BaseOrderId;
        BaseOrderType = snapshot.BaseOrderType;
    }

    private bool ApplySourceState(ReceivingOrderImportSnapshot snapshot)
    {
        bool changed = DeletionMark != snapshot.DeletionMark
            || Posted != snapshot.Posted;
        DeletionMark = snapshot.DeletionMark;
        Posted = snapshot.Posted;
        return changed;
    }

    private static OperationResult ValidateAudit(
        DateTimeOffset occurredAtUtc,
        string userId,
        string missingUserMessage)
    {
        if (occurredAtUtc == default)
        {
            return OperationError.Invalid("Время операции обязательно.");
        }

        return string.IsNullOrWhiteSpace(userId)
            ? OperationError.Invalid(missingUserMessage)
            : OperationResult.Success();
    }
}

public sealed record ReceivingJoinOutcome(
    Guid CycleId, Guid ReceivingLocationId, bool Started, bool Joined);
