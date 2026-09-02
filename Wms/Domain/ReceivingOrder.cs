using Wms.Common;
using Wms.Domain.Enums;

namespace Wms.Domain;

public class ReceivingOrder
{
    private readonly List<ReceivingOrderItem> _items = [];

    private ReceivingOrder()
    {
    }

    public Guid Id { get; private set; }
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
    public PutawayStatus PutawayStatus { get; private set; }
    public DateTimeOffset? PutawayStartedAtUtc { get; private set; }
    public DateTimeOffset? PutawayCompletedAtUtc { get; private set; }
    public string? PutawayStartedBy { get; private set; }
    public string? PutawayCompletedBy { get; private set; }
    public OrderSynchronizationLevel ExternalSynchronizationLevel { get; private set; }
    public DateTimeOffset? ExternalSynchronizationCheckedAtUtc { get; private set; }
    public DateTimeOffset? ExternalSynchronizationDetectedAtUtc { get; private set; }
    public string? ExternalSynchronizationFingerprint { get; private set; }
    public string? ExternalSynchronizationAcknowledgedFingerprint { get; private set; }
    public DateTimeOffset? ExternalSynchronizationAcknowledgedAtUtc { get; private set; }
    public string? ExternalSynchronizationAcknowledgedBy { get; private set; }
    public bool ExternalChangeDetected =>
        ExternalSynchronizationLevel != OrderSynchronizationLevel.Synchronized;
    public Guid ShipperId { get; private set; }
    public PartyType ShipperType { get; private set; }
    public PartyInfo? Shipper { get; private set; }
    public Guid BaseOrderId { get; private set; }
    public string? BaseOrderType { get; private set; }
    public IReadOnlyCollection<ReceivingOrderItem> Items => _items;

    public bool IsFullyReceived => _items.All(x => x.IsFullyReceived);
    public bool HasPlanFactDifference => _items.Any(x => x.IsPlanFactDifference);
    public double KnownFactWeightKg => _items.Sum(x => x.FactWeightKg ?? 0);
    public bool IsFactWeightComplete => _items.All(x => x.FactQuantity is decimal factQuantity
        && (factQuantity == 0 || x.FactWeightKg.HasValue));
    public int UnconfirmedItemCount => _items.Count(x => !x.IsFactConfirmed);

    public static OperationResult<ReceivingOrder> Create(
        ReceivingOrderImportSnapshot snapshot,
        DateTimeOffset createdAtUtc)
    {
        var validationResult = ValidateImport(snapshot, createdAtUtc);
        if (!validationResult.IsSuccess)
        {
            return validationResult.Error!;
        }

        if (snapshot.Status != ReceivingOrderStatus.ReadyForReceiving)
        {
            return OperationError.Invalid(
                "Приходный ордер можно создать только в статусе готовности к приёмке.");
        }

        if (snapshot.Items.Any(x => x.Quantity != x.PlanQuantity))
        {
            return OperationError.Invalid(
                "Приходный ордер можно создать, только если количество и количество упаковок в строках 1С совпадают.");
        }

        var order = new ReceivingOrder
        {
            Id = snapshot.Id,
            CreatedAtUtc = createdAtUtc,
            PutawayStatus = PutawayStatus.Inactive,
            ExternalSynchronizationLevel = OrderSynchronizationLevel.Synchronized,
            ExternalSynchronizationCheckedAtUtc = createdAtUtc
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

        order.ExternalSynchronizationFingerprint =
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
            ReceivingOrderSynchronizationComparer.Compare(this, snapshot);
        bool synchronizationStateChanged = ApplySynchronizationAssessment(
            assessment,
            updatedAtUtc);

        if (assessment.Level != OrderSynchronizationLevel.Synchronized)
        {
            return ReceivingOrderReconciliation.Conflict;
        }

        return synchronizationStateChanged
            ? ReceivingOrderReconciliation.Updated
            : ReceivingOrderReconciliation.Unchanged;
    }

    internal bool ApplySynchronizationAssessment(
        OrderSynchronizationAssessment assessment,
        DateTimeOffset checkedAtUtc)
    {
        DateTimeOffset? detectedAtUtc = assessment.Level == OrderSynchronizationLevel.Synchronized
            ? null
            : ExternalSynchronizationLevel == assessment.Level
                && ExternalSynchronizationFingerprint == assessment.Fingerprint
                    ? ExternalSynchronizationDetectedAtUtc
                    : checkedAtUtc;

        bool changed = ExternalSynchronizationLevel != assessment.Level
            || ExternalSynchronizationCheckedAtUtc != checkedAtUtc
            || ExternalSynchronizationDetectedAtUtc != detectedAtUtc
            || ExternalSynchronizationFingerprint != assessment.Fingerprint;

        ExternalSynchronizationLevel = assessment.Level;
        ExternalSynchronizationCheckedAtUtc = checkedAtUtc;
        ExternalSynchronizationDetectedAtUtc = detectedAtUtc;
        ExternalSynchronizationFingerprint = assessment.Fingerprint;

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
        Status = snapshot.Status;
        Queue = snapshot.Queue;
        ShipperId = snapshot.ShipperId;
        ShipperType = snapshot.ShipperType;
        ExternalSynchronizationLevel = OrderSynchronizationLevel.Synchronized;
        ExternalSynchronizationCheckedAtUtc = acknowledgedAtUtc;
        ExternalSynchronizationDetectedAtUtc = null;
        ExternalSynchronizationFingerprint = assessment.Fingerprint;
        ExternalSynchronizationAcknowledgedFingerprint = assessment.Fingerprint;
        ExternalSynchronizationAcknowledgedAtUtc = acknowledgedAtUtc;
        ExternalSynchronizationAcknowledgedBy = userId;
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
            or ReceivingOrderStatus.InReceiving
            or ReceivingOrderStatus.ProcessingRequired))
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

    public OperationResult UpdateItemFact(
        int lineNumber,
        decimal factQuantity,
        string? comment)
    {
        var editingResult = ValidateReceivingEditing();
        if (!editingResult.IsSuccess)
        {
            return editingResult;
        }

        var item = _items.FirstOrDefault(x => x.LineNumber == lineNumber);
        if (item is null)
        {
            return OperationError.NotFound(
                $"Строка {lineNumber} приходного ордера '{Id}' не найдена.");
        }

        var result = item.UpdateFact(factQuantity, comment);
        if (result.IsSuccess)
        {
            AdvanceOperationalRevision();
        }

        return result;
    }

    public OperationResult IncrementItemFact(int lineNumber)
    {
        var editingResult = ValidateReceivingEditing();
        if (!editingResult.IsSuccess)
        {
            return editingResult;
        }

        var item = _items.FirstOrDefault(x => x.LineNumber == lineNumber);
        if (item is null)
        {
            return OperationError.NotFound(
                $"Строка {lineNumber} приходного ордера '{Id}' не найдена.");
        }

        var result = item.IncrementFact();
        if (result.IsSuccess)
        {
            AdvanceOperationalRevision();
        }

        return result;
    }

    public OperationResult UpdateItemFactQuantity(int lineNumber, decimal factQuantity)
    {
        var editingResult = ValidateReceivingEditing();
        if (!editingResult.IsSuccess)
        {
            return editingResult;
        }

        var item = _items.FirstOrDefault(x => x.LineNumber == lineNumber);
        if (item is null)
        {
            return OperationError.NotFound(
                $"Строка {lineNumber} приходного ордера '{Id}' не найдена.");
        }

        var result = item.UpdateFact(factQuantity, item.Comment);
        if (result.IsSuccess)
        {
            AdvanceOperationalRevision();
        }

        return result;
    }

    public OperationResult UpdateItemComment(int lineNumber, string? comment)
    {
        var editingResult = ValidateReceivingEditing();
        if (!editingResult.IsSuccess)
        {
            return editingResult;
        }

        var item = _items.FirstOrDefault(x => x.LineNumber == lineNumber);
        if (item is null)
        {
            return OperationError.NotFound(
                $"Строка {lineNumber} приходного ордера '{Id}' не найдена.");
        }

        item.UpdateComment(comment);
        AdvanceOperationalRevision();
        return OperationResult.Success();
    }

    private OperationResult ValidateReceivingEditing() =>
        Status is ReceivingOrderStatus.InReceiving or ReceivingOrderStatus.ProcessingRequired
            ? OperationResult.Success()
            : OperationError.Invalid(
                "Строки приходного ордера можно изменять только во время приёмки или обработки ордера.");

    private OperationResult ValidateToSetReceived()
    {
        if (Status is not (ReceivingOrderStatus.InReceiving
            or ReceivingOrderStatus.ProcessingRequired))
        {
            return OperationError.Invalid(
                "Завершить приёмку можно только для ордера в приёмке или обработке.");
        }

        if (ReceivingLocationId is null)
        {
            return OperationError.Invalid(
                "Перед завершением приёмки необходимо указать позицию приёмки.");
        }

        if (_items.Any(x => !x.IsFactConfirmed))
        {
            return OperationError.Invalid(
                "Перед завершением приёмки необходимо проверить фактическое количество каждой строки.");
        }

        return OperationResult.Success();
    }

    public OperationResult SetReceived(DateTimeOffset completedAtUtc, string completedBy)
    {
        var validationResult = ValidateToSetReceived();
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

        Status = ReceivingOrderStatus.Received;
        CompletedAtUtc = completedAtUtc;
        CompletedBy = completedBy.Trim();
        PutawayStatus = _items.Any(x => x.FactQuantity > 0)
            ? PutawayStatus.Pending
            : PutawayStatus.Inactive;
        AdvanceOperationalRevision();
        return OperationResult.Success();
    }

    private OperationResult ValidateToStartPutaway()
    {
        if (Status != ReceivingOrderStatus.Received)
        {
            return OperationError.Invalid("Размещать можно только принятый ордер.");
        }

        if (PutawayStatus != PutawayStatus.Pending)
        {
            return OperationError.Invalid("Начать можно только ожидающее размещение.");
        }

        if (ReceivingLocationId is null)
        {
            return OperationError.Invalid(
                "Перед началом размещения необходимо указать позицию приёмки.");
        }

        if (!_items.Any(x => x.FactQuantity > 0))
        {
            return OperationError.Invalid(
                "Для размещения необходимо положительное принятое количество.");
        }

        return OperationResult.Success();
    }

    public OperationResult StartPutaway(DateTimeOffset startedAtUtc, string startedBy)
    {
        var validationResult = ValidateToStartPutaway();
        if (!validationResult.IsSuccess)
        {
            return validationResult;
        }

        var auditResult = ValidateAudit(startedAtUtc, startedBy, "Необходимо указать пользователя, начавшего операцию.");
        if (!auditResult.IsSuccess)
        {
            return auditResult;
        }

        if (startedAtUtc < CompletedAtUtc)
        {
            return OperationError.Invalid(
                "Время начала размещения не может предшествовать завершению приёмки.");
        }

        PutawayStatus = PutawayStatus.InProgress;
        PutawayStartedAtUtc = startedAtUtc;
        PutawayStartedBy = startedBy.Trim();
        AdvanceOperationalRevision();
        return OperationResult.Success();
    }

    public OperationResult<InventoryMovement> CreatePutawayMovement(
        Guid movementId,
        int lineNumber,
        Guid destinationStorageLocationId,
        decimal quantity,
        DateTimeOffset createdAtUtc,
        IReadOnlyCollection<InventoryMovement> draftMovements)
    {
        var editingResult = ValidatePutawayEditing();
        if (!editingResult.IsSuccess)
        {
            return editingResult.Error!;
        }

        var item = _items.FirstOrDefault(x => x.LineNumber == lineNumber);
        if (item is null)
        {
            return OperationError.NotFound(
                $"Строка {lineNumber} приходного ордера '{Id}' не найдена.");
        }

        var quantityResult = ValidatePutawayLineQuantity(item, quantity, draftMovements, null);
        if (!quantityResult.IsSuccess)
        {
            return quantityResult.Error!;
        }

        var movementResult = InventoryMovement.Create(
            movementId,
            WarehouseId,
            ReceivingLocationId,
            destinationStorageLocationId,
            item.StockKeepingUnitId,
            quantity,
            createdAtUtc,
            RecorderType.ReceivingOrder,
            Id,
            item.LineNumber);
        if (movementResult.IsSuccess)
        {
            AdvanceOperationalRevision();
        }

        return movementResult;
    }

    public OperationResult UpdatePutawayMovement(
        InventoryMovement movement,
        Guid destinationStorageLocationId,
        decimal quantity,
        DateTimeOffset updatedAtUtc,
        IReadOnlyCollection<InventoryMovement> draftMovements)
    {
        var movementResult = ValidatePutawayMovementChange(movement);
        if (!movementResult.IsSuccess)
        {
            return movementResult;
        }

        var item = _items.FirstOrDefault(x => x.LineNumber == movement.RecorderLineNumber);
        if (item is null)
        {
            return OperationError.NotFound(
                $"Строка {movement.RecorderLineNumber} приходного ордера '{Id}' для движения '{movement.Id}' не найдена.");
        }

        var quantityResult = ValidatePutawayLineQuantity(item, quantity, draftMovements, movement.Id);
        if (!quantityResult.IsSuccess)
        {
            return quantityResult;
        }

        var result = movement.UpdateDraft(
            ReceivingLocationId,
            destinationStorageLocationId,
            item.StockKeepingUnitId,
            quantity,
            updatedAtUtc);
        if (result.IsSuccess)
        {
            AdvanceOperationalRevision();
        }

        return result;
    }

    public OperationResult RemovePutawayMovement(InventoryMovement movement)
    {
        var result = ValidatePutawayMovementChange(movement);
        if (result.IsSuccess)
        {
            AdvanceOperationalRevision();
        }

        return result;
    }

    public OperationResult CompletePutaway(
        IReadOnlyCollection<InventoryMovement> draftMovements,
        DateTimeOffset completedAtUtc,
        string completedBy)
    {
        var completionResult = ValidatePutawayCompletion(draftMovements);
        if (!completionResult.IsSuccess)
        {
            return completionResult;
        }

        var auditResult = ValidateAudit(completedAtUtc, completedBy, "Необходимо указать пользователя, завершившего операцию.");
        if (!auditResult.IsSuccess)
        {
            return auditResult;
        }

        if (completedAtUtc < PutawayStartedAtUtc)
        {
            return OperationError.Invalid(
                "Время завершения размещения не может предшествовать его началу.");
        }

        PutawayStatus = PutawayStatus.Completed;
        PutawayCompletedAtUtc = completedAtUtc;
        PutawayCompletedBy = completedBy.Trim();
        AdvanceOperationalRevision();
        return OperationResult.Success();
    }

    private void AdvanceOperationalRevision() => OperationalRevision++;

    private OperationResult ValidatePutawayEditing()
    {
        if (Status != ReceivingOrderStatus.Received
            || PutawayStatus != PutawayStatus.InProgress)
        {
            return OperationError.Invalid(
                "Движения размещения можно изменять только во время размещения.");
        }

        if (ReceivingLocationId is null)
        {
            return OperationError.Invalid(
                "Для размещения необходимо указать позицию приёмки.");
        }

        return OperationResult.Success();
    }

    private OperationResult ValidatePutawayMovementChange(InventoryMovement movement)
    {
        var editingResult = ValidatePutawayEditing();
        if (!editingResult.IsSuccess)
        {
            return editingResult;
        }

        var draftResult = movement.ValidateDraft();
        if (!draftResult.IsSuccess)
        {
            return draftResult;
        }

        if (movement.RecorderType != RecorderType.ReceivingOrder
            || movement.RecorderId != Id
            || movement.RecorderLineNumber is null
            || movement.SourceStorageLocationId is null
            || movement.DestinationStorageLocationId is null)
        {
            return OperationError.Invalid(
                "Движение не относится к строке размещения приходного ордера.");
        }

        return OperationResult.Success();
    }

    private OperationResult ValidatePutawayCompletion(
        IReadOnlyCollection<InventoryMovement> draftMovements)
    {
        var editingResult = ValidatePutawayEditing();
        if (!editingResult.IsSuccess)
        {
            return editingResult;
        }

        if (draftMovements.Count == 0)
        {
            return OperationError.Invalid("В размещении отсутствуют движения.");
        }

        if (draftMovements.Any(x => x.PostedAtUtc is not null
            || x.RecorderType != RecorderType.ReceivingOrder
            || x.RecorderId != Id
            || x.WarehouseId != WarehouseId
            || x.SourceStorageLocationId != ReceivingLocationId
            || x.DestinationStorageLocationId is null))
        {
            return OperationError.Invalid("Размещение содержит некорректное движение.");
        }

        foreach (var item in _items)
        {
            var movements = draftMovements
                .Where(x => x.RecorderLineNumber == item.LineNumber)
                .ToList();

            if (movements.Any(x => x.StockKeepingUnitId != item.StockKeepingUnitId)
                || movements.Sum(x => x.Quantity) != item.FactQuantity!.Value)
            {
                return OperationError.Invalid(
                    "Перед завершением размещения каждая строка ордера должна быть размещена полностью.");
            }
        }

        if (draftMovements.Any(x => _items.All(item => item.LineNumber != x.RecorderLineNumber)))
        {
            return OperationError.Invalid(
                "Размещение содержит движение для неизвестной строки ордера.");
        }

        return OperationResult.Success();
    }

    private static OperationResult ValidatePutawayLineQuantity(
        ReceivingOrderItem item,
        decimal quantity,
        IReadOnlyCollection<InventoryMovement> draftMovements,
        Guid? excludedMovementId)
    {
        if (!WarehouseQuantity.IsPositive(quantity))
        {
            return OperationError.Invalid(
                "Количество размещения должно быть конечным числом больше нуля.");
        }

        var lineQuantity = draftMovements
            .Where(x => x.Id != excludedMovementId
                && x.RecorderLineNumber == item.LineNumber)
            .Sum(x => x.Quantity) + quantity;

        return lineQuantity <= item.FactQuantity!.Value
            ? OperationResult.Success()
            : OperationError.Invalid(
                "Количество размещения превышает принятое количество по строке ордера.");
    }

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

    private bool HasExternalChanges(ReceivingOrderImportSnapshot snapshot)
    {
        if (snapshot.Items is null)
        {
            return true;
        }

        if (BaseOrderId != snapshot.BaseOrderId
            || BaseOrderType != snapshot.BaseOrderType
            || Status != snapshot.Status
            || Queue != snapshot.Queue
            || BusinessOperation != snapshot.BusinessOperation
            || WarehouseOperation != snapshot.WarehouseOperation
            || Comment != snapshot.Comment
            || Posted != snapshot.Posted
            || DeletionMark != snapshot.DeletionMark
            || Date != snapshot.Date
            || Number != snapshot.Number
            || WarehouseId != snapshot.WarehouseId
            || ShipperId != snapshot.ShipperId
            || ShipperType != snapshot.ShipperType
            || _items.Count != snapshot.Items.Count)
        {
            return true;
        }

        var importedItems = snapshot.Items.ToLookup(x => x.LineNumber);
        foreach (var existingItem in _items)
        {
            var importedLine = importedItems[existingItem.LineNumber].ToList();
            if (importedLine.Count != 1
                || existingItem.StockKeepingUnitId != importedLine[0].StockKeepingUnitId
                || existingItem.PlanQuantity != importedLine[0].PlanQuantity)
            {
                return true;
            }
        }

        return false;
    }

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
        DeletionMark = snapshot.DeletionMark;
        Posted = snapshot.Posted;
        Number = snapshot.Number;
        Date = snapshot.Date;
        WarehouseId = snapshot.WarehouseId;
        Comment = snapshot.Comment;
        Status = snapshot.Status;
        Queue = snapshot.Queue;
        WarehouseOperation = snapshot.WarehouseOperation;
        BusinessOperation = snapshot.BusinessOperation;
        ShipperId = snapshot.ShipperId;
        ShipperType = snapshot.ShipperType;
        BaseOrderId = snapshot.BaseOrderId;
        BaseOrderType = snapshot.BaseOrderType;
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
