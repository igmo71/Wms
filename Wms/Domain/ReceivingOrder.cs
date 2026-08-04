using Wms.Common;
using Wms.Domain.Enums;

namespace Wms.Domain;

public class ReceivingOrder
{
    public Guid Id { get; set; }
    public bool Posted { get; set; }
    public bool DeletionMark { get; set; }
    public DateTime Date { get; set; }
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
    public DateTimeOffset? ExternalChangeDetectedAtUtc { get; set; }

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

    public bool AllowExternalUpdate(WmsSettings settings) =>
    Status switch
    {
        ReceivingOrderStatus.Pending => settings.AllowExternalUpdatePending,
        ReceivingOrderStatus.InProcess => settings.AllowExternalUpdateInProcess,
        ReceivingOrderStatus.ProcessingRequired => settings.AllowExternalUpdateInProcess,
        ReceivingOrderStatus.Completed => settings.AllowExternalUpdateCompleted,
        _ => false
    };

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
            || DeletionMark != externalOrder.DeletionMark
            || Date != externalOrder.Date
            || Number != externalOrder.Number
            || WarehouseId != externalOrder.WarehouseId
            || SenderId != externalOrder.SenderId
            || SenderType != externalOrder.SenderType)
        {
            return true;
        }

        return HaveImportItemChanges(externalOrder.Items);
    }

    private bool HaveImportItemChanges(List<ReceivingOrderItem> externalItems)
    {
        if (Items.Count != externalItems.Count)
            return true;

        var externalByLineNumber = externalItems
            .ToDictionary(x => x.LineNumber);

        foreach (var existing in Items)
        {
            if (!externalByLineNumber.TryGetValue(existing.LineNumber, out var external))
            {
                return true;
            }

            if (existing.StockKeepingUnitId != external.StockKeepingUnitId
                || existing.PlanQuantity != external.PlanQuantity)
            {
                return true;
            }
        }

        return false;
    }

    public void UpdateFromImport(ReceivingOrder externaOrder)
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
        Date = externaOrder.Date;
        Number = externaOrder.Number;
        WarehouseId = externaOrder.WarehouseId;
        SenderId = externaOrder.SenderId;
        SenderType = externaOrder.SenderType;

        UpdateOrderItems(externaOrder.Items);
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

    public void MarkExternalChangeDetected(DateTimeOffset detectedAtUtc)
    {
        ExternalChangeDetectedAtUtc = detectedAtUtc;
    }

    public void ClearExternalChangeDetected()
    {
        ExternalChangeDetectedAtUtc = null;
    }
}
