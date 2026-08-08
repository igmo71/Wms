using Wms.Common;
using Wms.Domain.Enums;

namespace Wms.Domain;

public class ShippingOrder
{
    public Guid Id { get; set; }
    public bool DeletionMark { get; set; }
    public bool Posted { get; set; }
    public string? Number { get; set; }
    public DateTime Date { get; set; }

    public Guid WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }

    public Guid? ShippingLocationId { get; set; }
    public StorageLocation? ShippingLocation { get; set; }

    public string? Comment { get; set; }

    public ShippingOrderStatus Status { get; set; }
    public ShippingOrderQueue Queue { get; set; }
    public DateTime PlannedShippingDate { get; set; }
    public Guid? DeliveryDirectionId { get; set; }
    public DeliveryDirection? DeliveryDirection { get; set; }
    public WarehouseOperation WarehouseOperation { get; set; }


    public DateTimeOffset? CreatedAtUtc { get; set; }
    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }

    public string? StartedBy { get; set; }
    public string? CompletedBy { get; set; }

    public bool ExternalChangeDetected { get; set; }

    public Guid RecipientId { get; set; }
    public string? RecipientType { get; set; }

    public List<ShippingOrderBaseItem> BaseItems { get; set; } = [];
    public List<ShippingOrderItem> Items { get; set; } = [];

    public bool IsFullyShipped => Items.All(x => x.IsFullyShipped);
    public bool HasPlanFactDifference => Items.Any(x => x.IsPlanFactDifference);

    internal bool AllowExternalCreate(WmsSettings wmsSettings) =>
    Status switch
    {
        ShippingOrderStatus.Completed => wmsSettings.AllowExternalCreateCompleted,
        _ => true
    };

    public bool AllowExternalUpdate(WmsSettings wmsSettings) =>
    Status switch
    {
        ShippingOrderStatus.Pending => wmsSettings.AllowExternalUpdatePending,
        ShippingOrderStatus.InProcess => wmsSettings.AllowExternalUpdateInProcess,
        ShippingOrderStatus.ForVerification => wmsSettings.AllowExternalUpdateInProcess,
        ShippingOrderStatus.InVerification => wmsSettings.AllowExternalUpdateInProcess,
        ShippingOrderStatus.Verified => wmsSettings.AllowExternalUpdateInProcess,
        ShippingOrderStatus.ForShipment => wmsSettings.AllowExternalUpdateInProcess,
        ShippingOrderStatus.Completed => wmsSettings.AllowExternalUpdateCompleted,
        _ => false
    };

    internal bool HasExternalChanges(ShippingOrder externalOrder)
    {
        if (Status != externalOrder.Status
            || Queue != externalOrder.Queue
            || WarehouseOperation != externalOrder.WarehouseOperation
            || Comment != externalOrder.Comment
            || Posted != externalOrder.Posted
            || DeletionMark != externalOrder.DeletionMark
            || Date != externalOrder.Date
            || Number != externalOrder.Number
            || WarehouseId != externalOrder.WarehouseId
            || PlannedShippingDate != externalOrder.PlannedShippingDate
            || DeliveryDirectionId != externalOrder.DeliveryDirectionId
            || RecipientId != externalOrder.RecipientId
            || RecipientType != externalOrder.RecipientType)
        {
            return true;
        }

        if (Items.Count != externalOrder.Items.Count)
            return true;

        if (BaseItems.Count != externalOrder.BaseItems.Count)
            return true;

        var externalItemsByLineNumber = externalOrder.Items.ToDictionary(x => x.LineNumber);

        foreach (var existingItem in Items)
        {
            if (!externalItemsByLineNumber.TryGetValue(existingItem.LineNumber, out var external))
            {
                return true;
            }

            if (existingItem.StockKeepingUnitId != external.StockKeepingUnitId
                || existingItem.PlanQuantity != external.PlanQuantity
                || existingItem.Action != external.Action)
            {
                return true;
            }
        }

        var externalBaseItemsByLineNumber = externalOrder.BaseItems.ToDictionary(x => x.LineNumber);

        foreach (var existingItem in BaseItems)
        {
            if (!externalBaseItemsByLineNumber.TryGetValue(existingItem.LineNumber, out var external))
            {
                return true;
            }

            if (existingItem.StockKeepingUnitId != external.StockKeepingUnitId
                || existingItem.PlanQuantity != external.PlanQuantity
                || existingItem.BaseOrderId != external.BaseOrderId
                || existingItem.BaseOrderType != external.BaseOrderType)
            {
                return true;
            }
        }

        return false;
    }

    internal void UpdateOrder(ShippingOrder externalOrder)
    {
        DeletionMark = externalOrder.DeletionMark;
        Posted = externalOrder.Posted;
        Number = externalOrder.Number;
        Date = externalOrder.Date;
        WarehouseId = externalOrder.WarehouseId;
        Comment = externalOrder.Comment;
        Status = externalOrder.Status;
        Queue = externalOrder.Queue;
        PlannedShippingDate = externalOrder.PlannedShippingDate;
        DeliveryDirectionId = externalOrder.DeliveryDirectionId;
        WarehouseOperation = externalOrder.WarehouseOperation;
        RecipientId = externalOrder.RecipientId;
        RecipientType = externalOrder.RecipientType;

        UpdateOrderItems(externalOrder.Items);

        UpdateOrderBaseItems(externalOrder.BaseItems);
    }

    private void UpdateOrderItems(List<ShippingOrderItem> externalOrderItems)
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
                existing.Action = external.Action;
            }
            else
            {
                Items.Add(new ShippingOrderItem
                {
                    ShippingOrderId = Id,
                    LineNumber = external.LineNumber,
                    StockKeepingUnitId = external.StockKeepingUnitId,
                    PlanQuantity = external.PlanQuantity,
                    FactQuantity = 0,
                    Action = external.Action
                });
            }
        }
    }

    private void UpdateOrderBaseItems(List<ShippingOrderBaseItem> externalOrderBaseItems)
    {
        var externalByLineNumber = externalOrderBaseItems.ToDictionary(item => item.LineNumber);

        BaseItems.RemoveAll(existing => !externalByLineNumber.ContainsKey(existing.LineNumber));

        var existingByLineNumber = BaseItems.ToDictionary(item => item.LineNumber);

        foreach (var external in externalOrderBaseItems)
        {
            if (existingByLineNumber.TryGetValue(external.LineNumber, out var existing))
            {
                existing.StockKeepingUnitId = external.StockKeepingUnitId;
                existing.PlanQuantity = external.PlanQuantity;
                existing.BaseOrderId = external.BaseOrderId;
                existing.BaseOrderType = external.BaseOrderType;
            }
            else
            {
                BaseItems.Add(new ShippingOrderBaseItem
                {
                    ShippingOrderId = Id,
                    LineNumber = external.LineNumber,
                    StockKeepingUnitId = external.StockKeepingUnitId,
                    PlanQuantity = external.PlanQuantity,
                    BaseOrderId = external.BaseOrderId,
                    BaseOrderType = external.BaseOrderType
                });
            }
        }
    }

    internal ServiceResult ValidateToStart()
    {
        if (Status != ShippingOrderStatus.Pending)
        {
            return ServiceError.Invalid<ShippingOrder>("Only a pending shipping order can be started.");
        }

        if (ShippingLocationId is null)
        {
            return ServiceError.Invalid<ShippingOrder>("Shipping location must be specified before starting the order.");
        }

        return ServiceResult.Success();
    }

    public void Start(string userId)
    {
        Status = ShippingOrderStatus.InProcess;

        StartedAtUtc = DateTimeOffset.UtcNow;

        StartedBy = userId;
    }

    public ServiceResult ValidateToComplete()
    {
        if (Status is not (ShippingOrderStatus.InProcess
                or ShippingOrderStatus.ForVerification
                or ShippingOrderStatus.InVerification
                or ShippingOrderStatus.Verified
                or ShippingOrderStatus.ForShipment))
        {
            return ServiceError.Invalid<ShippingOrder>("Shipping order cannot be completed in the current status.");
        }

        if (ShippingLocationId is null)
        {
            return ServiceError.Invalid<ShippingOrder>("Shipping location must be specified before completing the order.");
        }

        return ServiceResult.Success();
    }

    public void Complete(string userId)
    {
        Status = ShippingOrderStatus.Completed;

        CompletedAtUtc = DateTimeOffset.UtcNow;

        CompletedBy = userId;
    }
}
