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
    public bool ExternalChangeDetected { get; private set; }
    public Guid ShipperId { get; private set; }
    public PartyType ShipperType { get; private set; }
    public PartyInfo? Shipper { get; private set; }
    public Guid BaseOrderId { get; private set; }
    public string? BaseOrderType { get; private set; }
    public IReadOnlyCollection<ReceivingOrderItem> Items => _items;

    public bool IsFullyReceived => _items.All(x => x.IsFullyReceived);
    public bool HasPlanFactDifference => _items.Any(x => x.IsPlanFactDifference);
    public double KnownFactWeightKg => _items.Sum(x => x.FactWeightKg ?? 0);
    public bool IsFactWeightComplete => _items.All(x => x.FactQuantity == 0 || x.FactWeightKg.HasValue);

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
            return OperationError.Invalid<ReceivingOrder>(
                "A receiving order can be created only when it is ready for receiving.");
        }

        var order = new ReceivingOrder
        {
            Id = snapshot.Id,
            CreatedAtUtc = createdAtUtc,
            PutawayStatus = PutawayStatus.Inactive
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

        return order;
    }

    public OperationResult<ReceivingOrderReconciliation> Reconcile(
        ReceivingOrderImportSnapshot snapshot,
        DateTimeOffset updatedAtUtc)
    {
        if (snapshot.Id != Id)
        {
            return OperationError.Invalid<ReceivingOrder>(
                "Imported receiving order must match the existing order.");
        }

        if (!HasExternalChanges(snapshot))
        {
            return ReceivingOrderReconciliation.Unchanged;
        }

        if (Status != ReceivingOrderStatus.ReadyForReceiving)
        {
            ExternalChangeDetected = true;
            return ReceivingOrderReconciliation.Conflict;
        }

        var validationResult = ValidateImport(snapshot, updatedAtUtc);
        if (!validationResult.IsSuccess)
        {
            return validationResult.Error!;
        }

        if (updatedAtUtc < CreatedAtUtc)
        {
            return OperationError.Invalid<ReceivingOrder>(
                "Receiving order update time cannot precede its creation time.");
        }

        var itemsResult = ReconcileItems(snapshot.Items);
        if (!itemsResult.IsSuccess)
        {
            return itemsResult.Error!;
        }

        ApplyImport(snapshot);
        UpdatedAtUtc = updatedAtUtc;
        ExternalChangeDetected = false;
        return ReceivingOrderReconciliation.Updated;
    }

    public OperationResult SetReceivingLocation(Guid receivingLocationId)
    {
        if (receivingLocationId == Guid.Empty)
        {
            return OperationError.Invalid<StorageLocation>("Receiving location identifier is required.");
        }

        if (Status is not (ReceivingOrderStatus.ReadyForReceiving
            or ReceivingOrderStatus.InReceiving
            or ReceivingOrderStatus.ProcessingRequired))
        {
            return OperationError.Invalid<ReceivingOrder>(
                "Receiving location can be changed only before the order is received.");
        }

        ReceivingLocationId = receivingLocationId;
        return OperationResult.Success();
    }

    private OperationResult ValidateToSetInReceiving()
    {
        if (Status != ReceivingOrderStatus.ReadyForReceiving)
        {
            return OperationError.Invalid<ReceivingOrder>(
                "Only a receiving order ready for receiving can be set in receiving.");
        }

        if (ReceivingLocationId is null)
        {
            return OperationError.Invalid<ReceivingOrder>(
                "Receiving location must be specified before setting the order in receiving.");
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

        var auditResult = ValidateAudit(startedAtUtc, startedBy, "Starting user must be specified.");
        if (!auditResult.IsSuccess)
        {
            return auditResult;
        }

        if (startedAtUtc < CreatedAtUtc)
        {
            return OperationError.Invalid<ReceivingOrder>(
                "Receiving start time cannot precede order creation.");
        }

        Status = ReceivingOrderStatus.InReceiving;
        StartedAtUtc = startedAtUtc;
        StartedBy = startedBy.Trim();
        return OperationResult.Success();
    }

    public OperationResult UpdateItemFact(
        int lineNumber,
        double factQuantity,
        string? comment)
    {
        if (Status is not (ReceivingOrderStatus.InReceiving
            or ReceivingOrderStatus.ProcessingRequired))
        {
            return OperationError.Invalid<ReceivingOrderItem>(
                "Fact quantity can be edited only while the receiving order is in receiving or requires processing.");
        }

        var item = _items.FirstOrDefault(x => x.LineNumber == lineNumber);
        return item is null
            ? OperationError.NotFound<ReceivingOrderItem>()
            : item.UpdateFact(factQuantity, comment);
    }

    private OperationResult ValidateToSetReceived()
    {
        if (Status is not (ReceivingOrderStatus.InReceiving
            or ReceivingOrderStatus.ProcessingRequired))
        {
            return OperationError.Invalid<ReceivingOrder>(
                "Only a receiving order in receiving or requiring processing can be set received.");
        }

        if (ReceivingLocationId is null)
        {
            return OperationError.Invalid<ReceivingOrder>(
                "Receiving location must be specified before receiving the order.");
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

        var auditResult = ValidateAudit(completedAtUtc, completedBy, "Completing user must be specified.");
        if (!auditResult.IsSuccess)
        {
            return auditResult;
        }

        if (completedAtUtc < CreatedAtUtc || completedAtUtc < StartedAtUtc)
        {
            return OperationError.Invalid<ReceivingOrder>(
                "Receiving completion time cannot precede earlier order operations.");
        }

        Status = ReceivingOrderStatus.Received;
        CompletedAtUtc = completedAtUtc;
        CompletedBy = completedBy.Trim();
        PutawayStatus = _items.Any(x => x.FactQuantity > 0)
            ? PutawayStatus.Pending
            : PutawayStatus.Inactive;
        return OperationResult.Success();
    }

    private OperationResult ValidateToStartPutaway()
    {
        if (Status != ReceivingOrderStatus.Received)
        {
            return OperationError.Invalid<ReceivingOrder>("Only a received order can be put away.");
        }

        if (PutawayStatus != PutawayStatus.Pending)
        {
            return OperationError.Invalid<ReceivingOrder>("Only pending putaway can be started.");
        }

        if (ReceivingLocationId is null)
        {
            return OperationError.Invalid<ReceivingOrder>(
                "Receiving location must be specified before starting putaway.");
        }

        if (!_items.Any(x => x.FactQuantity > 0))
        {
            return OperationError.Invalid<ReceivingOrder>(
                "Putaway requires a positive received quantity.");
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

        var auditResult = ValidateAudit(startedAtUtc, startedBy, "Starting user must be specified.");
        if (!auditResult.IsSuccess)
        {
            return auditResult;
        }

        if (startedAtUtc < CompletedAtUtc)
        {
            return OperationError.Invalid<ReceivingOrder>(
                "Putaway start time cannot precede receiving completion.");
        }

        PutawayStatus = PutawayStatus.InProgress;
        PutawayStartedAtUtc = startedAtUtc;
        PutawayStartedBy = startedBy.Trim();
        return OperationResult.Success();
    }

    public OperationResult CompletePutaway(DateTimeOffset completedAtUtc, string completedBy)
    {
        if (Status != ReceivingOrderStatus.Received || PutawayStatus != PutawayStatus.InProgress)
        {
            return OperationError.Invalid<ReceivingOrder>(
                "Only in-progress putaway can be completed.");
        }

        var auditResult = ValidateAudit(completedAtUtc, completedBy, "Completing user must be specified.");
        if (!auditResult.IsSuccess)
        {
            return auditResult;
        }

        if (completedAtUtc < PutawayStartedAtUtc)
        {
            return OperationError.Invalid<ReceivingOrder>(
                "Putaway completion time cannot precede its start.");
        }

        PutawayStatus = PutawayStatus.Completed;
        PutawayCompletedAtUtc = completedAtUtc;
        PutawayCompletedBy = completedBy.Trim();
        return OperationResult.Success();
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
            return OperationError.Invalid<ReceivingOrder>(
                "Receiving order and warehouse identifiers are required.");
        }

        if (snapshot.Date == default)
        {
            return OperationError.Invalid<ReceivingOrder>("Receiving order date is required.");
        }

        if (occurredAtUtc == default)
        {
            return OperationError.Invalid<ReceivingOrder>("Import time is required.");
        }

        if (snapshot.Items is null
            || snapshot.Items.GroupBy(x => x.LineNumber).Any(x => x.Count() > 1))
        {
            return OperationError.Invalid<ReceivingOrderItem>(
                "Receiving order item line numbers must be unique.");
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
            return OperationError.Invalid<ReceivingOrder>("Operation time is required.");
        }

        return string.IsNullOrWhiteSpace(userId)
            ? OperationError.Invalid<ReceivingOrder>(missingUserMessage)
            : OperationResult.Success();
    }
}
