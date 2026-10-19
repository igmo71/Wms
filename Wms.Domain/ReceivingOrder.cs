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
    public int ActiveCycleNumber { get; private set; }
    public long OperationalRevision { get; private set; }
    public Guid WarehouseId { get; private set; }
    public Warehouse? Warehouse { get; private set; }
    public Guid? ReceivingLocationId { get; private set; }
    public StorageLocation? ReceivingLocation { get; private set; }
    public ReceivingOrderStatus Status { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? StartedAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public string? StartedBy { get; private set; }
    public string? CompletedBy { get; private set; }
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
        if (Status != ReceivingOrderStatus.InReceiving)
            return OperationError.Conflict("Ордер не находится в активном цикле приёмки.");
        var number = ActiveCycleNumber;
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
        var number = ActiveCycleNumber;
        if (Status != ReceivingOrderStatus.Received || number == int.MaxValue)
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
        if (Status is not (ReceivingOrderStatus.ReadyForReceiving or ReceivingOrderStatus.InReceiving))
            return OperationError.Conflict("У ордера нет действующего цикла приёмки.");
        var number = ActiveCycleNumber;
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
        Status == ReceivingOrderStatus.InReceiving
            ? _cycles.SingleOrDefault(x => x.Number == ActiveCycleNumber && x.CompletedAtUtc == null
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

    private void AdvanceOperationalRevision() => OperationalRevision++;


}

public sealed record ReceivingJoinOutcome(
    Guid CycleId, Guid ReceivingLocationId, bool Started, bool Joined);
