using Wms.Common;
using Wms.Domain.Enums;

namespace Wms.Domain;

public class ReceivingOrder
{
    public Guid Id { get; set; }
    public bool DeletionMark { get; set; }
    public bool Posted { get; set; }
    public string? Number { get; set; }
    public DateTime Date { get; set; }

    public Guid WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }

    public Guid? ReceivingLocationId { get; set; }
    public StorageLocation? ReceivingLocation { get; set; }

    public string? Comment { get; set; }

    public ReceivingOrderStatus Status { get; set; }
    public ReceivingOrderQueue Queue { get; set; }
    public WarehouseOperation WarehouseOperation { get; set; }
    public BusinessOperation BusinessOperation { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }

    public string? StartedBy { get; set; }
    public string? CompletedBy { get; set; }

    public PutawayStatus PutawayStatus { get; set; }
    public DateTimeOffset? PutawayStartedAtUtc { get; set; }
    public DateTimeOffset? PutawayCompletedAtUtc { get; set; }
    public string? PutawayStartedBy { get; set; }
    public string? PutawayCompletedBy { get; set; }

    public bool ExternalChangeDetected { get; set; }

    public Guid ShipperId { get; set; }
    public string? ShipperType { get; set; }

    public Guid BaseOrderId { get; set; }
    public string? BaseOrderType { get; set; }

    public List<ReceivingOrderItem> Items { get; set; } = [];

    public bool IsFullyReceived => Items.All(x => x.IsFullyReceived);
    public bool HasPlanFactDifference => Items.Any(x => x.IsPlanFactDifference);
    public double KnownFactWeightKg => Items.Sum(x => x.FactWeightKg ?? 0);
    public bool IsFactWeightComplete => Items.All(x => x.FactQuantity == 0 || x.FactWeightKg.HasValue);

    public bool HasExternalChanges(ReceivingOrder externalOrder)
    {
        if (BaseOrderId != externalOrder.BaseOrderId
            || BaseOrderType != externalOrder.BaseOrderType
            || Status != externalOrder.Status
            || Queue != externalOrder.Queue
            || BusinessOperation != externalOrder.BusinessOperation
            || WarehouseOperation != externalOrder.WarehouseOperation
            || Comment != externalOrder.Comment
            || Posted != externalOrder.Posted
            || DeletionMark != externalOrder.DeletionMark
            || Date != externalOrder.Date
            || Number != externalOrder.Number
            || WarehouseId != externalOrder.WarehouseId
            || ShipperId != externalOrder.ShipperId
            || ShipperType != externalOrder.ShipperType)
        {
            return true;
        }


        if (Items.Count != externalOrder.Items.Count)
            return true;

        var externalItemsByLineNumber = externalOrder.Items.ToDictionary(x => x.LineNumber);

        foreach (var existingItem in Items)
        {
            if (!externalItemsByLineNumber.TryGetValue(existingItem.LineNumber, out var external))
            {
                return true;
            }

            if (existingItem.StockKeepingUnitId != external.StockKeepingUnitId
                || existingItem.PlanQuantity != external.PlanQuantity)
            {
                return true;
            }
        }

        return false;
    }

    public void UpdateOrder(ReceivingOrder externalOrder)
    {
        DeletionMark = externalOrder.DeletionMark;
        Posted = externalOrder.Posted;
        Number = externalOrder.Number;
        Date = externalOrder.Date;
        WarehouseId = externalOrder.WarehouseId;
        WarehouseOperation = externalOrder.WarehouseOperation;
        Comment = externalOrder.Comment;
        Status = externalOrder.Status;
        Queue = externalOrder.Queue;
        WarehouseOperation = externalOrder.WarehouseOperation;
        BusinessOperation = externalOrder.BusinessOperation;
        ShipperId = externalOrder.ShipperId;
        ShipperType = externalOrder.ShipperType;
        BaseOrderId = externalOrder.BaseOrderId;
        BaseOrderType = externalOrder.BaseOrderType;

        UpdateOrderItems(externalOrder.Items);
    }

    private void UpdateOrderItems(List<ReceivingOrderItem> externalOrderItems)
    {
        var externalByLineNumber = externalOrderItems.ToDictionary(item => item.LineNumber);

        Items.RemoveAll(existing => !externalByLineNumber.ContainsKey(existing.LineNumber));

        var existingByLineNumber = Items.ToDictionary(item => item.LineNumber);

        foreach (var external in externalOrderItems)
        {
            if (existingByLineNumber.TryGetValue(external.LineNumber, out var existing))
            {
                existing.StockKeepingUnitId = external.StockKeepingUnitId;
                existing.PlanQuantity = external.PlanQuantity;
            }
            else
            {
                Items.Add(new ReceivingOrderItem
                {
                    ReceivingOrderId = Id,
                    LineNumber = external.LineNumber,
                    StockKeepingUnitId = external.StockKeepingUnitId,
                    PlanQuantity = external.PlanQuantity,
                    FactQuantity = 0
                });
            }
        }
    }

    public ServiceResult ValidateToSetInReceiving()
    {
        if (Status != ReceivingOrderStatus.ReadyForReceiving)
        {
            return ServiceError.Invalid<ReceivingOrder>("Only a receiving order ready for receiving can be set in receiving.");
        }

        if (ReceivingLocationId is null)
        {
            return ServiceError.Invalid<ReceivingOrder>("Receiving location must be specified before setting the order in receiving.");
        }

        return ServiceResult.Success();
    }

    public void SetInReceiving(string userId)
    {
        Status = ReceivingOrderStatus.InReceiving;

        StartedAtUtc = DateTimeOffset.UtcNow;

        StartedBy = userId;
    }

    public ServiceResult ValidateToSetReceived()
    {
        var canSetReceived = Status is ReceivingOrderStatus.InReceiving or ReceivingOrderStatus.ProcessingRequired;

        if (!canSetReceived)
        {
            return ServiceError.Invalid<ReceivingOrder>("Only a receiving order in receiving or requiring processing can be set received.");
        }

        if (ReceivingLocationId is null)
        {
            return ServiceError.Invalid<ReceivingOrder>("Receiving location must be specified before receiving the order.");
        }

        return ServiceResult.Success();
    }

    public void SetReceived(string userId)
    {
        Status = ReceivingOrderStatus.Received;

        CompletedAtUtc = DateTimeOffset.UtcNow;

        CompletedBy = userId;

        if (Items.Any(x => x.FactQuantity > 0))
            PutawayStatus = PutawayStatus.Pending;
    }

    public ServiceResult ValidateToStartPutaway()
    {
        if (Status != ReceivingOrderStatus.Received)
            return ServiceError.Invalid<ReceivingOrder>("Only a received order can be put away.");

        if (PutawayStatus != PutawayStatus.Pending)
            return ServiceError.Invalid<ReceivingOrder>("Only pending putaway can be started.");

        if (ReceivingLocationId is null)
            return ServiceError.Invalid<ReceivingOrder>("Receiving location must be specified before starting putaway.");

        if (!Items.Any(x => x.FactQuantity > 0))
            return ServiceError.Invalid<ReceivingOrder>("Putaway requires a positive received quantity.");

        return ServiceResult.Success();
    }

    public void StartPutaway(string userId)
    {
        PutawayStatus = PutawayStatus.InProgress;
        PutawayStartedAtUtc = DateTimeOffset.UtcNow;
        PutawayStartedBy = userId;
    }

    public void CompletePutaway(string userId)
    {
        PutawayStatus = PutawayStatus.Completed;
        PutawayCompletedAtUtc = DateTimeOffset.UtcNow;
        PutawayCompletedBy = userId;
    }
}
