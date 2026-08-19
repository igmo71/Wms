using Wms.Common;
using Wms.Domain.Enums;

namespace Wms.Domain;

public class ShippingOrder
{
    private readonly List<ShippingOrderBaseItem> _baseItems = [];
    private readonly List<ShippingOrderItem> _items = [];

    private ShippingOrder()
    {
    }

    public Guid Id { get; private set; }
    public bool DeletionMark { get; private set; }
    public bool Posted { get; private set; }
    public string? Number { get; private set; }
    public DateTime Date { get; private set; }
    public Guid WarehouseId { get; private set; }
    public Warehouse? Warehouse { get; private set; }
    public Guid? ShippingLocationId { get; private set; }
    public StorageLocation? ShippingLocation { get; private set; }
    public string? Comment { get; private set; }
    public ShippingOrderStatus Status { get; private set; }
    public ShippingOrderQueue Queue { get; private set; }
    public DateTime? PlannedShippingDate { get; private set; }
    public Guid? DeliveryDirectionId { get; private set; }
    public DeliveryDirection? DeliveryDirection { get; private set; }
    public WarehouseOperation WarehouseOperation { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }
    public DateTimeOffset? PickingStartedAtUtc { get; private set; }
    public DateTimeOffset? ReadyForShipmentAtUtc { get; private set; }
    public DateTimeOffset? ShippedAtUtc { get; private set; }
    public DateTimeOffset? RolledBackAtUtc { get; private set; }
    public string? PickingStartedBy { get; private set; }
    public string? ReadyForShipmentBy { get; private set; }
    public string? ShippedBy { get; private set; }
    public string? RolledBackBy { get; private set; }
    public string? RollbackReason { get; private set; }
    public bool ExternalChangeDetected { get; private set; }
    public Guid ReceiverId { get; private set; }
    public PartyType ReceiverType { get; private set; }
    public PartyInfo? Receiver { get; private set; }
    public IReadOnlyCollection<ShippingOrderBaseItem> BaseItems => _baseItems;
    public IReadOnlyCollection<ShippingOrderItem> Items => _items;

    public bool IsFullyShipped => _items.All(x => x.IsFullyShipped);
    public double KnownFactWeightKg => _items.Sum(x => x.FactWeightKg ?? 0);
    public bool IsFactWeightComplete => _items.All(x => x.FactQuantity == 0 || x.FactWeightKg.HasValue);

    public static OperationResult<ShippingOrder> Create(
        ShippingOrderImportSnapshot snapshot,
        DateTimeOffset createdAtUtc)
    {
        OperationResult validationResult = ValidateImport(snapshot, createdAtUtc);
        if (!validationResult.IsSuccess)
        {
            return validationResult.Error!;
        }

        if (snapshot.Status != ShippingOrderStatus.Prepared)
        {
            return OperationError.Invalid<ShippingOrder>(
                "A shipping order can be created only when it is prepared.");
        }

        var order = new ShippingOrder
        {
            Id = snapshot.Id,
            CreatedAtUtc = createdAtUtc
        };

        order.ApplyImport(snapshot);
        foreach (ShippingOrderItemImportSnapshot itemSnapshot in snapshot.Items)
        {
            OperationResult<ShippingOrderItem> itemResult = ShippingOrderItem.Create(order.Id, itemSnapshot);
            if (!itemResult.IsSuccess)
            {
                return itemResult.Error!;
            }

            order._items.Add(itemResult.Value!);
        }

        foreach (ShippingOrderBaseItemImportSnapshot itemSnapshot in snapshot.BaseItems)
        {
            OperationResult<ShippingOrderBaseItem> itemResult = ShippingOrderBaseItem.Create(order.Id, itemSnapshot);
            if (!itemResult.IsSuccess)
            {
                return itemResult.Error!;
            }

            order._baseItems.Add(itemResult.Value!);
        }

        return order;
    }

    public OperationResult<ShippingOrderReconciliation> Reconcile(
        ShippingOrderImportSnapshot snapshot,
        DateTimeOffset updatedAtUtc)
    {
        if (snapshot.Id != Id)
        {
            return OperationError.Invalid<ShippingOrder>(
                "Imported shipping order identifier does not match the existing order.");
        }

        if (!HasExternalChanges(snapshot))
        {
            return ShippingOrderReconciliation.Unchanged;
        }

        if (Status != ShippingOrderStatus.Prepared)
        {
            ExternalChangeDetected = true;
            return ShippingOrderReconciliation.Conflict;
        }

        OperationResult validationResult = ValidateImport(snapshot, updatedAtUtc);
        if (!validationResult.IsSuccess)
        {
            return validationResult.Error!;
        }

        if (updatedAtUtc < CreatedAtUtc)
        {
            return OperationError.Invalid<ShippingOrder>(
                "Shipping order update time cannot precede its creation time.");
        }

        OperationResult itemsResult = ReconcileItems(snapshot.Items);
        if (!itemsResult.IsSuccess)
        {
            return itemsResult.Error!;
        }

        OperationResult baseItemsResult = ReconcileBaseItems(snapshot.BaseItems);
        if (!baseItemsResult.IsSuccess)
        {
            return baseItemsResult.Error!;
        }

        ApplyImport(snapshot);
        UpdatedAtUtc = updatedAtUtc;
        ExternalChangeDetected = false;
        return ShippingOrderReconciliation.Updated;
    }

    public OperationResult SetShippingLocation(Guid shippingLocationId)
    {
        if (shippingLocationId == Guid.Empty)
        {
            return OperationError.Invalid<StorageLocation>(
                "Shipping location identifier is required.");
        }

        if (Status != ShippingOrderStatus.Prepared)
        {
            return OperationError.Invalid<ShippingOrder>(
                "Shipping location can be changed only while the order is prepared.");
        }

        ShippingLocationId = shippingLocationId;
        return OperationResult.Success();
    }

    public OperationResult SetReadyForPicking(DateTimeOffset startedAtUtc, string startedBy)
    {
        if (Status != ShippingOrderStatus.Prepared)
        {
            return OperationError.Invalid<ShippingOrder>(
                "Only a prepared shipping order can be set ready for picking.");
        }

        if (ShippingLocationId is null)
        {
            return OperationError.Invalid<ShippingOrder>(
                "Shipping location must be specified before setting the order ready for picking.");
        }

        OperationResult auditResult = ValidateAudit(startedAtUtc, startedBy, "Picking user must be specified.");
        if (!auditResult.IsSuccess)
        {
            return auditResult;
        }

        if (startedAtUtc < CreatedAtUtc)
        {
            return OperationError.Invalid<ShippingOrder>(
                "Picking start time cannot precede order creation.");
        }

        Status = ShippingOrderStatus.ReadyForPicking;
        PickingStartedAtUtc = startedAtUtc;
        PickingStartedBy = startedBy.Trim();
        return OperationResult.Success();
    }

    public OperationResult<InventoryMovement> CreatePickingMovement(
        Guid movementId,
        int lineNumber,
        Guid sourceStorageLocationId,
        double quantity,
        DateTimeOffset createdAtUtc,
        IReadOnlyCollection<InventoryMovement> draftMovements)
    {
        OperationResult editingResult = ValidatePickingEditing();
        if (!editingResult.IsSuccess)
        {
            return editingResult.Error!;
        }

        OperationResult draftsResult = ValidatePickingDraftMovements(draftMovements);
        if (!draftsResult.IsSuccess)
        {
            return draftsResult.Error!;
        }

        ShippingOrderItem? item = _items.FirstOrDefault(x => x.LineNumber == lineNumber);
        if (item is null)
        {
            return OperationError.NotFound<ShippingOrderItem>();
        }

        OperationResult<double> factResult = CalculatePickingLineFact(item, quantity, draftMovements, null);
        if (!factResult.IsSuccess)
        {
            return factResult.Error!;
        }

        OperationResult<InventoryMovement> movementResult = InventoryMovement.Create(
            movementId,
            WarehouseId,
            sourceStorageLocationId,
            ShippingLocationId,
            item.StockKeepingUnitId,
            quantity,
            createdAtUtc,
            RecorderType.ShippingOrder,
            Id,
            item.LineNumber);
        if (!movementResult.IsSuccess)
        {
            return movementResult.Error!;
        }

        OperationResult itemResult = item.UpdateFact(factResult.Value);
        return itemResult.IsSuccess
            ? movementResult.Value!
            : itemResult.Error!;
    }

    public OperationResult UpdatePickingMovement(
        InventoryMovement movement,
        Guid sourceStorageLocationId,
        double quantity,
        DateTimeOffset updatedAtUtc,
        IReadOnlyCollection<InventoryMovement> draftMovements)
    {
        OperationResult movementResult = ValidatePickingMovementChange(movement);
        if (!movementResult.IsSuccess)
        {
            return movementResult;
        }

        OperationResult draftsResult = ValidatePickingDraftMovements(draftMovements);
        if (!draftsResult.IsSuccess)
        {
            return draftsResult;
        }

        if (draftMovements.All(x => x.Id != movement.Id))
        {
            return OperationError.Invalid<InventoryMovement>(
                "Picking movement is not part of the order drafts.");
        }

        ShippingOrderItem? item = _items.FirstOrDefault(x => x.LineNumber == movement.RecorderLineNumber);
        if (item is null)
        {
            return OperationError.NotFound<ShippingOrderItem>();
        }

        OperationResult<double> factResult = CalculatePickingLineFact(item, quantity, draftMovements, movement.Id);
        if (!factResult.IsSuccess)
        {
            return factResult.Error!;
        }

        OperationResult updateResult = movement.UpdateDraft(
            sourceStorageLocationId,
            ShippingLocationId,
            item.StockKeepingUnitId,
            quantity,
            updatedAtUtc);
        if (!updateResult.IsSuccess)
        {
            return updateResult;
        }

        return item.UpdateFact(factResult.Value);
    }

    public OperationResult RemovePickingMovement(
        InventoryMovement movement,
        IReadOnlyCollection<InventoryMovement> draftMovements)
    {
        OperationResult movementResult = ValidatePickingMovementChange(movement);
        if (!movementResult.IsSuccess)
        {
            return movementResult;
        }

        OperationResult draftsResult = ValidatePickingDraftMovements(draftMovements);
        if (!draftsResult.IsSuccess)
        {
            return draftsResult;
        }

        if (draftMovements.All(x => x.Id != movement.Id))
        {
            return OperationError.Invalid<InventoryMovement>(
                "Picking movement is not part of the order drafts.");
        }

        ShippingOrderItem? item = _items.FirstOrDefault(x => x.LineNumber == movement.RecorderLineNumber);
        if (item is null)
        {
            return OperationError.NotFound<ShippingOrderItem>();
        }

        double factQuantity = draftMovements
            .Where(x => x.Id != movement.Id
                && x.RecorderLineNumber == item.LineNumber)
            .Sum(x => x.Quantity);

        return item.UpdateFact(factQuantity);
    }

    public OperationResult SetReadyForShipment(
        IReadOnlyCollection<InventoryMovement> draftMovements,
        DateTimeOffset readyAtUtc,
        string readyBy)
    {
        bool canSetReady = Status is ShippingOrderStatus.ReadyForPicking
            or ShippingOrderStatus.ReadyForVerification
            or ShippingOrderStatus.InVerification
            or ShippingOrderStatus.Verified;
        if (!canSetReady)
        {
            return OperationError.Invalid<ShippingOrder>(
                "Only a shipping order being picked or verified can be set ready for shipment.");
        }

        OperationResult pickingResult = ValidatePickingCompletion(draftMovements);
        if (!pickingResult.IsSuccess)
        {
            return pickingResult;
        }

        OperationResult auditResult = ValidateAudit(readyAtUtc, readyBy, "Picking completion user must be specified.");
        if (!auditResult.IsSuccess)
        {
            return auditResult;
        }

        if (PickingStartedAtUtc is null || readyAtUtc < PickingStartedAtUtc)
        {
            return OperationError.Invalid<ShippingOrder>(
                "Ready-for-shipment time cannot precede picking start.");
        }

        Status = ShippingOrderStatus.ReadyForShipment;
        ReadyForShipmentAtUtc = readyAtUtc;
        ReadyForShipmentBy = readyBy.Trim();
        return OperationResult.Success();
    }

    public OperationResult SetShipped(DateTimeOffset shippedAtUtc, string shippedBy)
    {
        if (Status != ShippingOrderStatus.ReadyForShipment)
        {
            return OperationError.Invalid<ShippingOrder>(
                "Only a shipping order ready for shipment can be shipped.");
        }

        if (ShippingLocationId is null)
        {
            return OperationError.Invalid<ShippingOrder>(
                "Shipping location must be specified before shipping the order.");
        }

        OperationResult auditResult = ValidateAudit(shippedAtUtc, shippedBy, "Shipping user must be specified.");
        if (!auditResult.IsSuccess)
        {
            return auditResult;
        }

        if (ReadyForShipmentAtUtc is null || shippedAtUtc < ReadyForShipmentAtUtc)
        {
            return OperationError.Invalid<ShippingOrder>(
                "Shipping time cannot precede readiness for shipment.");
        }

        Status = ShippingOrderStatus.Shipped;
        ShippedAtUtc = shippedAtUtc;
        ShippedBy = shippedBy.Trim();
        return OperationResult.Success();
    }

    public OperationResult<List<InventoryMovement>> Rollback(
        string reason,
        string userId,
        DateTimeOffset rolledBackAtUtc,
        IReadOnlyCollection<InventoryMovement> draftMovements,
        IReadOnlyCollection<InventoryMovement> postedMovements)
    {
        if (Status is ShippingOrderStatus.Prepared or ShippingOrderStatus.Shipped)
        {
            return OperationError.Invalid<ShippingOrder>(
                "Only a shipping order in progress or ready for shipment can be rolled back.");
        }

        if (PickingStartedAtUtc is null)
        {
            return OperationError.Failure<ShippingOrder>(
                "Shipping order has no picking start time and cannot be rolled back safely.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return OperationError.Invalid<ShippingOrder>("Rollback reason must be specified.");
        }

        OperationResult auditResult = ValidateAudit(rolledBackAtUtc, userId, "Rollback user must be specified.");
        if (!auditResult.IsSuccess)
        {
            return auditResult.Error!;
        }

        if (rolledBackAtUtc < PickingStartedAtUtc)
        {
            return OperationError.Invalid<ShippingOrder>(
                "Rollback time cannot precede picking start.");
        }

        if (draftMovements.Any(x => x.PostedAtUtc is not null
            || x.RecorderType != RecorderType.ShippingOrder
            || x.RecorderId != Id))
        {
            return OperationError.Invalid<InventoryMovement>(
                "Shipping order contains an invalid draft picking movement.");
        }

        var currentCycleMovements = postedMovements
            .Where(x => x.CreatedAtUtc >= PickingStartedAtUtc)
            .OrderByDescending(x => x.PostedAtUtc)
            .ToList();
        if (currentCycleMovements.Any(x => x.PostedAtUtc is null
            || x.RecorderType != RecorderType.ShippingOrder
            || x.RecorderId != Id
            || x.WarehouseId != WarehouseId
            || x.SourceStorageLocationId is null
            || x.DestinationStorageLocationId != ShippingLocationId
            || x.RecorderLineNumber is null))
        {
            return OperationError.Failure<ShippingOrder>(
                "Shipping order contains movements that cannot be rolled back safely.");
        }

        OperationResult<List<InventoryMovement>> compensationResult = CreateCompensationMovements(
            currentCycleMovements,
            rolledBackAtUtc);
        if (!compensationResult.IsSuccess)
        {
            return compensationResult.Error!;
        }

        Status = ShippingOrderStatus.Prepared;
        UpdatedAtUtc = rolledBackAtUtc;
        PickingStartedAtUtc = null;
        ReadyForShipmentAtUtc = null;
        PickingStartedBy = null;
        ReadyForShipmentBy = null;
        RolledBackAtUtc = rolledBackAtUtc;
        RolledBackBy = userId.Trim();
        RollbackReason = reason.Trim();

        foreach (ShippingOrderItem item in _items)
        {
            item.ResetFact();
        }

        return compensationResult.Value!;
    }

    private OperationResult ValidatePickingEditing()
    {
        bool isEditable = Status is ShippingOrderStatus.ReadyForPicking
            or ShippingOrderStatus.ReadyForVerification
            or ShippingOrderStatus.InVerification
            or ShippingOrderStatus.Verified;
        if (!isEditable)
        {
            return OperationError.Invalid<ShippingOrder>(
                "Picking movements can be changed only while the shipping order is being picked or verified.");
        }

        if (ShippingLocationId is null)
        {
            return OperationError.Invalid<ShippingOrder>(
                "Shipping location must be specified before changing picking movements.");
        }

        return OperationResult.Success();
    }

    private OperationResult ValidatePickingMovementChange(InventoryMovement movement)
    {
        OperationResult editingResult = ValidatePickingEditing();
        if (!editingResult.IsSuccess)
        {
            return editingResult;
        }

        OperationResult draftResult = movement.ValidateDraft();
        if (!draftResult.IsSuccess)
        {
            return draftResult;
        }

        if (movement.RecorderType != RecorderType.ShippingOrder
            || movement.RecorderId != Id
            || movement.RecorderLineNumber is null)
        {
            return OperationError.Invalid<InventoryMovement>(
                "Movement does not belong to a shipping order line.");
        }

        return OperationResult.Success();
    }

    private OperationResult ValidatePickingDraftMovements(
        IReadOnlyCollection<InventoryMovement> draftMovements)
    {
        return draftMovements.Any(x => x.PostedAtUtc is not null
            || x.RecorderType != RecorderType.ShippingOrder
            || x.RecorderId != Id)
            ? OperationError.Invalid<InventoryMovement>(
                "Picking contains an invalid draft movement.")
            : OperationResult.Success();
    }

    private OperationResult ValidatePickingCompletion(
        IReadOnlyCollection<InventoryMovement> draftMovements)
    {
        if (ShippingLocationId is null)
        {
            return OperationError.Invalid<ShippingOrder>(
                "Shipping location must be specified before completing picking.");
        }

        if (draftMovements.Any(x => x.PostedAtUtc is not null
            || x.RecorderType != RecorderType.ShippingOrder
            || x.RecorderId != Id
            || x.WarehouseId != WarehouseId
            || x.SourceStorageLocationId is null
            || x.DestinationStorageLocationId != ShippingLocationId
            || x.RecorderLineNumber is null
            || !double.IsFinite(x.Quantity)
            || x.Quantity <= 0))
        {
            return OperationError.Invalid<InventoryMovement>(
                "Picking contains an invalid movement.");
        }

        foreach (ShippingOrderItem item in _items)
        {
            var movements = draftMovements
                .Where(x => x.RecorderLineNumber == item.LineNumber)
                .ToList();
            if (movements.Any(x => x.StockKeepingUnitId != item.StockKeepingUnitId)
                || movements.Sum(x => x.Quantity) != item.FactQuantity)
            {
                return OperationError.Invalid<ShippingOrder>(
                    "Every shipping order line fact must match its picking movements.");
            }
        }

        if (draftMovements.Any(x => _items.All(item => item.LineNumber != x.RecorderLineNumber)))
        {
            return OperationError.Invalid<InventoryMovement>(
                "Picking contains a movement for an unknown order line.");
        }

        return OperationResult.Success();
    }

    private static OperationResult<double> CalculatePickingLineFact(
        ShippingOrderItem item,
        double quantity,
        IReadOnlyCollection<InventoryMovement> draftMovements,
        Guid? excludedMovementId)
    {
        if (!double.IsFinite(quantity) || quantity <= 0)
        {
            return OperationError.Invalid<InventoryMovement>(
                "Picking quantity must be a finite number greater than zero.");
        }

        double lineQuantity = draftMovements
            .Where(x => x.Id != excludedMovementId
                && x.RecorderLineNumber == item.LineNumber)
            .Sum(x => x.Quantity) + quantity;

        return lineQuantity <= item.PlanQuantity
            ? lineQuantity
            : OperationError.Invalid<InventoryMovement>(
                "Picking quantity exceeds the planned quantity for the shipping order line.");
    }

    private OperationResult<List<InventoryMovement>> CreateCompensationMovements(
        IEnumerable<InventoryMovement> postedMovements,
        DateTimeOffset createdAtUtc)
    {
        var movements = new List<InventoryMovement>();
        foreach (InventoryMovement postedMovement in postedMovements)
        {
            OperationResult<InventoryMovement> movementResult = InventoryMovement.Create(
                Guid.NewGuid(),
                postedMovement.WarehouseId,
                postedMovement.DestinationStorageLocationId,
                postedMovement.SourceStorageLocationId,
                postedMovement.StockKeepingUnitId,
                postedMovement.Quantity,
                createdAtUtc,
                RecorderType.ShippingOrder,
                Id,
                postedMovement.RecorderLineNumber);
            if (!movementResult.IsSuccess)
            {
                return movementResult.Error!;
            }

            movements.Add(movementResult.Value!);
        }

        return movements;
    }

    internal void SetReceiver(PartyInfo? receiver)
    {
        Receiver = receiver;
    }

    private static OperationResult ValidateImport(
        ShippingOrderImportSnapshot snapshot,
        DateTimeOffset occurredAtUtc)
    {
        if (snapshot.Id == Guid.Empty || snapshot.WarehouseId == Guid.Empty)
        {
            return OperationError.Invalid<ShippingOrder>(
                "Shipping order and warehouse identifiers are required.");
        }

        if (snapshot.Date == default)
        {
            return OperationError.Invalid<ShippingOrder>("Shipping order date is required.");
        }

        if (occurredAtUtc == default)
        {
            return OperationError.Invalid<ShippingOrder>("Import time is required.");
        }

        if (snapshot.Items is null
            || snapshot.Items.GroupBy(x => x.LineNumber).Any(x => x.Count() > 1))
        {
            return OperationError.Invalid<ShippingOrderItem>(
                "Shipping order item line numbers must be unique.");
        }

        if (snapshot.BaseItems is null
            || snapshot.BaseItems.GroupBy(x => x.LineNumber).Any(x => x.Count() > 1))
        {
            return OperationError.Invalid<ShippingOrderBaseItem>(
                "Shipping order base item line numbers must be unique.");
        }

        foreach (ShippingOrderItemImportSnapshot itemSnapshot in snapshot.Items)
        {
            OperationResult itemResult = ShippingOrderItem.ValidateImport(snapshot.Id, itemSnapshot);
            if (!itemResult.IsSuccess)
            {
                return itemResult;
            }
        }

        foreach (ShippingOrderBaseItemImportSnapshot itemSnapshot in snapshot.BaseItems)
        {
            OperationResult itemResult = ShippingOrderBaseItem.ValidateImport(snapshot.Id, itemSnapshot);
            if (!itemResult.IsSuccess)
            {
                return itemResult;
            }
        }

        return OperationResult.Success();
    }

    private bool HasExternalChanges(ShippingOrderImportSnapshot snapshot)
    {
        if (snapshot.Items is null || snapshot.BaseItems is null)
        {
            return true;
        }

        if (Status != snapshot.Status
            || Queue != snapshot.Queue
            || WarehouseOperation != snapshot.WarehouseOperation
            || Comment != snapshot.Comment
            || Posted != snapshot.Posted
            || DeletionMark != snapshot.DeletionMark
            || Date != snapshot.Date
            || Number != snapshot.Number
            || WarehouseId != snapshot.WarehouseId
            || PlannedShippingDate != snapshot.PlannedShippingDate
            || DeliveryDirectionId != snapshot.DeliveryDirectionId
            || ReceiverId != snapshot.ReceiverId
            || ReceiverType != snapshot.ReceiverType
            || _items.Count != snapshot.Items.Count
            || _baseItems.Count != snapshot.BaseItems.Count)
        {
            return true;
        }

        ILookup<int, ShippingOrderItemImportSnapshot> importedItems = snapshot.Items.ToLookup(x => x.LineNumber);
        foreach (ShippingOrderItem existingItem in _items)
        {
            var importedLine = importedItems[existingItem.LineNumber].ToList();
            if (importedLine.Count != 1
                || existingItem.StockKeepingUnitId != importedLine[0].StockKeepingUnitId
                || existingItem.PlanQuantity != importedLine[0].PlanQuantity)
            {
                return true;
            }
        }

        ILookup<int, ShippingOrderBaseItemImportSnapshot> importedBaseItems = snapshot.BaseItems.ToLookup(x => x.LineNumber);
        foreach (ShippingOrderBaseItem existingItem in _baseItems)
        {
            var importedLine = importedBaseItems[existingItem.LineNumber].ToList();
            if (importedLine.Count != 1
                || existingItem.StockKeepingUnitId != importedLine[0].StockKeepingUnitId
                || existingItem.PlanQuantity != importedLine[0].PlanQuantity
                || existingItem.BaseOrderId != importedLine[0].BaseOrderId
                || existingItem.BaseOrderType != importedLine[0].BaseOrderType)
            {
                return true;
            }
        }

        return false;
    }

    private OperationResult ReconcileItems(
        IReadOnlyCollection<ShippingOrderItemImportSnapshot> snapshots)
    {
        var importedItems = snapshots.ToDictionary(x => x.LineNumber);
        _items.RemoveAll(existingItem => !importedItems.ContainsKey(existingItem.LineNumber));

        var existingItems = _items.ToDictionary(x => x.LineNumber);
        foreach (ShippingOrderItemImportSnapshot snapshot in snapshots)
        {
            if (existingItems.TryGetValue(snapshot.LineNumber, out ShippingOrderItem? existingItem))
            {
                OperationResult itemResult = existingItem.Reconcile(snapshot);
                if (!itemResult.IsSuccess)
                {
                    return itemResult;
                }
            }
            else
            {
                OperationResult<ShippingOrderItem> itemResult = ShippingOrderItem.Create(Id, snapshot);
                if (!itemResult.IsSuccess)
                {
                    return itemResult.Error!;
                }

                _items.Add(itemResult.Value!);
            }
        }

        return OperationResult.Success();
    }

    private OperationResult ReconcileBaseItems(
        IReadOnlyCollection<ShippingOrderBaseItemImportSnapshot> snapshots)
    {
        var importedItems = snapshots.ToDictionary(x => x.LineNumber);
        _baseItems.RemoveAll(existingItem => !importedItems.ContainsKey(existingItem.LineNumber));

        var existingItems = _baseItems.ToDictionary(x => x.LineNumber);
        foreach (ShippingOrderBaseItemImportSnapshot snapshot in snapshots)
        {
            if (existingItems.TryGetValue(snapshot.LineNumber, out ShippingOrderBaseItem? existingItem))
            {
                OperationResult itemResult = existingItem.Reconcile(snapshot);
                if (!itemResult.IsSuccess)
                {
                    return itemResult;
                }
            }
            else
            {
                OperationResult<ShippingOrderBaseItem> itemResult = ShippingOrderBaseItem.Create(Id, snapshot);
                if (!itemResult.IsSuccess)
                {
                    return itemResult.Error!;
                }

                _baseItems.Add(itemResult.Value!);
            }
        }

        return OperationResult.Success();
    }

    private void ApplyImport(ShippingOrderImportSnapshot snapshot)
    {
        DeletionMark = snapshot.DeletionMark;
        Posted = snapshot.Posted;
        Number = snapshot.Number;
        Date = snapshot.Date;
        WarehouseId = snapshot.WarehouseId;
        Comment = snapshot.Comment;
        Status = snapshot.Status;
        Queue = snapshot.Queue;
        PlannedShippingDate = snapshot.PlannedShippingDate;
        DeliveryDirectionId = snapshot.DeliveryDirectionId;
        WarehouseOperation = snapshot.WarehouseOperation;
        ReceiverId = snapshot.ReceiverId;
        ReceiverType = snapshot.ReceiverType;
    }

    private static OperationResult ValidateAudit(
        DateTimeOffset occurredAtUtc,
        string userId,
        string missingUserMessage)
    {
        if (occurredAtUtc == default)
        {
            return OperationError.Invalid<ShippingOrder>("Operation time is required.");
        }

        return string.IsNullOrWhiteSpace(userId)
            ? OperationError.Invalid<ShippingOrder>(missingUserMessage)
            : OperationResult.Success();
    }
}
