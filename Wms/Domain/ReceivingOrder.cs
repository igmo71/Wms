using Wms.Common;
using Wms.Domain.Enums;

namespace Wms.Domain;

public class ReceivingOrder
{
    public Guid Id { get; set; }
    public bool Posted { get; set; }
    public bool DeletionMark { get; set; }
    public DateTime DateTime { get; set; }
    public string? Number { get; set; }
    public string? Comment { get; set; }
    public Guid? WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }

    public Guid? ReceivingLocationId { get; set; }
    public StorageLocation? ReceivingLocation { get; set; }
    public ReceivingOrderStatus Status { get; set; }
    public ReceivingOrderQueue Queue { get; set; }
    public WarehouseOperation WarehouseOperation { get; set; }
    public BusinessOperation BusinessOperation { get; set; }

    public DateTimeOffset? CreatedAtUtc { get; set; }
    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public DateTimeOffset? ExternalChangeDetectedAtUtc { get; set; } // Прилетал запрос из 1С

    public Guid? StartedBy { get; set; }
    public Guid? CompletedBy { get; set; }

    public Guid? SenderId { get; set; }
    public string? SenderType { get; set; }

    public Guid? BaseOrderId { get; set; }
    public string? BaseOrderType { get; set; }

    public List<ReceivingOrderItem> Items { get; set; } = [];


    public bool CanStart => StartedAtUtc is null && CompletedAtUtc is null;
    public bool CanComplete => StartedAtUtc is not null && CompletedAtUtc is null;

    public bool ExternalChangeDetected => ExternalChangeDetectedAtUtc is not null;

    public bool AllowExternalUpdate(WmsSettings wmsSettings) =>
        (Status == ReceivingOrderStatus.Pending && wmsSettings.AllowExternalUpdatePending) ||
        (Status == ReceivingOrderStatus.InProcess && wmsSettings.AllowExternalUpdateInProcess) ||
        (Status == ReceivingOrderStatus.Completed && wmsSettings.AllowExternalUpdateCompleted);

    public bool IsFullyReceived => Items.All(x => x.IsFullyReceived);
    public bool HasPlanFactDifference => Items.Any(x => x.IsPlanFactDifference);

    public bool HasImportChanges(ReceivingOrder externalOrder)
    {
        if (BaseOrderId != externalOrder.BaseOrderId
            || BaseOrderType != externalOrder.BaseOrderType
            || Status != externalOrder.Status
            || Queue != externalOrder.Queue
            || BusinessOperation != externalOrder.BusinessOperation
            || WarehouseOperation != externalOrder.WarehouseOperation
            || Comment != externalOrder.Comment
            || Posted != externalOrder.Posted
            || DeletionMark != externalOrder.DeletionMark)
        {
            return true;
        }

        return HaveImportItemChanges(Items, externalOrder.Items);
    }

    public void Update(ReceivingOrder externaOrder)
    {
        BaseOrderId = externaOrder.BaseOrderId;
        BaseOrderType = externaOrder.BaseOrderType;
        Status = externaOrder.Status;
        Queue = externaOrder.Queue;
        BusinessOperation = externaOrder.BusinessOperation;
        WarehouseOperation = externaOrder.WarehouseOperation;
        Comment = externaOrder.Comment;
        Posted = externaOrder.Posted;
        DeletionMark = externaOrder.DeletionMark;

        UpdateOrderItems(Items, externaOrder.Items);
    }

    private static void UpdateOrderItems(
    List<ReceivingOrderItem> existingOrderItems,
    IReadOnlyCollection<ReceivingOrderItem> externalOrderItems)
    {
        var externalByKey = externalOrderItems
            .ToDictionary(item => (item.ReceivingOrderId, item.LineNumber));

        existingOrderItems
            .RemoveAll(existing => !externalByKey.ContainsKey((existing.ReceivingOrderId, existing.LineNumber)));

        var existingByKey = existingOrderItems
            .ToDictionary(item => (item.ReceivingOrderId, item.LineNumber));

        foreach (var external in externalOrderItems)
        {
            var key = (external.ReceivingOrderId, external.LineNumber);

            if (existingByKey.TryGetValue(key, out var existing))
            {
                existing.StockKeepingUnitId = external.StockKeepingUnitId;
                existing.PlanQuantity = external.PlanQuantity;
            }
            else
            {
                existingOrderItems.Add(new ReceivingOrderItem
                {
                    ReceivingOrderId = external.ReceivingOrderId,
                    LineNumber = external.LineNumber,
                    StockKeepingUnitId = external.StockKeepingUnitId,
                    PlanQuantity = external.PlanQuantity,
                    FactQuantity = 0
                });
            }
        }
    }
}
