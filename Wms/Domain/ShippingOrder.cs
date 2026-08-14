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
    public DateTime? PlannedShippingDate { get; set; }
    public Guid? DeliveryDirectionId { get; set; }
    public DeliveryDirection? DeliveryDirection { get; set; }
    public WarehouseOperation WarehouseOperation { get; set; }


    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public DateTimeOffset? PickingStartedAtUtc { get; set; }
    public DateTimeOffset? ReadyForShipmentAtUtc { get; set; }
    public DateTimeOffset? ShippedAtUtc { get; set; }

    public DateTimeOffset? RolledBackAtUtc { get; set; }

    public string? PickingStartedBy { get; set; }
    public string? ReadyForShipmentBy { get; set; }
    public string? ShippedBy { get; set; }
    public string? RolledBackBy { get; set; }
    public string? RollbackReason { get; set; }

    public bool ExternalChangeDetected { get; set; }

    public Guid RecipientId { get; set; }
    public string? RecipientType { get; set; }

    public List<ShippingOrderBaseItem> BaseItems { get; set; } = [];
    public List<ShippingOrderItem> Items { get; set; } = [];

    public bool IsFullyShipped => Items.All(x => x.IsFullyShipped);
    public bool HasPlanFactDifference => Items.Any(x => x.IsPlanFactDifference);
    public double KnownFactWeightKg => Items.Sum(x => x.FactWeightKg ?? 0);
    public bool IsFactWeightComplete => Items.All(x => x.FactQuantity == 0 || x.FactWeightKg.HasValue);

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
        WarehouseOperation = externalOrder.WarehouseOperation;
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

    internal ServiceResult ValidateToSetReadyForPicking()
    {
        if (Status != ShippingOrderStatus.Prepared)
        {
            return ServiceError.Invalid<ShippingOrder>("Only a prepared shipping order can be set ready for picking.");
        }

        if (ShippingLocationId is null)
        {
            return ServiceError.Invalid<ShippingOrder>("Shipping location must be specified before setting the order ready for picking.");
        }

        return ServiceResult.Success();
    }

    public void SetReadyForPicking(string userId)
    {
        Status = ShippingOrderStatus.ReadyForPicking;

        PickingStartedAtUtc = DateTimeOffset.UtcNow;

        PickingStartedBy = userId;
    }

    public ServiceResult ValidateToSetReadyForShipment()
    {
        var canSetReadyForShipment = Status is ShippingOrderStatus.ReadyForPicking
            or ShippingOrderStatus.ReadyForVerification
            or ShippingOrderStatus.InVerification
            or ShippingOrderStatus.Verified;

        if (!canSetReadyForShipment)
        {
            return ServiceError.Invalid<ShippingOrder>("Only a shipping order being picked or verified can be set ready for shipment.");
        }

        return ServiceResult.Success();
    }

    public void SetReadyForShipment(string userId)
    {
        Status = ShippingOrderStatus.ReadyForShipment;

        ReadyForShipmentAtUtc = DateTimeOffset.UtcNow;

        ReadyForShipmentBy = userId;
    }

    public ServiceResult ValidateToSetShipped()
    {
        if (Status != ShippingOrderStatus.ReadyForShipment)
        {
            return ServiceError.Invalid<ShippingOrder>("Only a shipping order ready for shipment can be shipped.");
        }

        if (ShippingLocationId is null)
        {
            return ServiceError.Invalid<ShippingOrder>("Shipping location must be specified before shipping the order.");
        }

        return ServiceResult.Success();
    }

    public void SetShipped(string userId)
    {
        Status = ShippingOrderStatus.Shipped;

        ShippedAtUtc = DateTimeOffset.UtcNow;

        ShippedBy = userId;
    }

    public ServiceResult ValidateToRollback()
    {
        if (Status is ShippingOrderStatus.Prepared or ShippingOrderStatus.Shipped)
        {
            return ServiceError.Invalid<ShippingOrder>(
                "Only a shipping order in progress or ready for shipment can be rolled back.");
        }

        if (PickingStartedAtUtc is null)
        {
            return ServiceError.Failure<ShippingOrder>(
                "Shipping order has no picking start time and cannot be rolled back safely.");
        }

        return ServiceResult.Success();
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

        foreach (var item in Items)
            item.FactQuantity = 0;
    }
}
