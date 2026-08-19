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
        var validationResult = ValidateImport(snapshot, createdAtUtc);
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
        foreach (var itemSnapshot in snapshot.Items)
        {
            var itemResult = ShippingOrderItem.Create(order.Id, itemSnapshot);
            if (!itemResult.IsSuccess)
            {
                return itemResult.Error!;
            }

            order._items.Add(itemResult.Value!);
        }

        foreach (var itemSnapshot in snapshot.BaseItems)
        {
            var itemResult = ShippingOrderBaseItem.Create(order.Id, itemSnapshot);
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

        var validationResult = ValidateImport(snapshot, updatedAtUtc);
        if (!validationResult.IsSuccess)
        {
            return validationResult.Error!;
        }

        if (updatedAtUtc < CreatedAtUtc)
        {
            return OperationError.Invalid<ShippingOrder>(
                "Shipping order update time cannot precede its creation time.");
        }

        var itemsResult = ReconcileItems(snapshot.Items);
        if (!itemsResult.IsSuccess)
        {
            return itemsResult.Error!;
        }

        var baseItemsResult = ReconcileBaseItems(snapshot.BaseItems);
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

        var auditResult = ValidateAudit(startedAtUtc, startedBy, "Picking user must be specified.");
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

    public OperationResult UpdateItemFact(int lineNumber, double factQuantity)
    {
        bool isEditable = Status is ShippingOrderStatus.ReadyForPicking
            or ShippingOrderStatus.ReadyForVerification
            or ShippingOrderStatus.InVerification
            or ShippingOrderStatus.Verified;
        if (!isEditable)
        {
            return OperationError.Invalid<ShippingOrder>(
                "Shipping facts can be changed only while the order is being picked or verified.");
        }

        var item = _items.FirstOrDefault(x => x.LineNumber == lineNumber);
        return item is null
            ? OperationError.NotFound<ShippingOrderItem>()
            : item.UpdateFact(factQuantity);
    }

    public OperationResult SetReadyForShipment(DateTimeOffset readyAtUtc, string readyBy)
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

        var auditResult = ValidateAudit(readyAtUtc, readyBy, "Picking completion user must be specified.");
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

        var auditResult = ValidateAudit(shippedAtUtc, shippedBy, "Shipping user must be specified.");
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

    public OperationResult ValidateToRollback()
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

        return OperationResult.Success();
    }

    public void Rollback(string reason, string userId)
    {
        Status = ShippingOrderStatus.Prepared;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
        PickingStartedAtUtc = null;
        ReadyForShipmentAtUtc = null;
        PickingStartedBy = null;
        ReadyForShipmentBy = null;
        RolledBackAtUtc = UpdatedAtUtc;
        RolledBackBy = userId;
        RollbackReason = reason;

        foreach (var item in _items)
        {
            item.ResetFact();
        }
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

        foreach (var itemSnapshot in snapshot.Items)
        {
            var itemResult = ShippingOrderItem.ValidateImport(snapshot.Id, itemSnapshot);
            if (!itemResult.IsSuccess)
            {
                return itemResult;
            }
        }

        foreach (var itemSnapshot in snapshot.BaseItems)
        {
            var itemResult = ShippingOrderBaseItem.ValidateImport(snapshot.Id, itemSnapshot);
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

        var importedBaseItems = snapshot.BaseItems.ToLookup(x => x.LineNumber);
        foreach (var existingItem in _baseItems)
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
                var itemResult = ShippingOrderItem.Create(Id, snapshot);
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
                var itemResult = ShippingOrderBaseItem.Create(Id, snapshot);
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
