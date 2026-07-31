using System.Linq.Expressions;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application;

public class ReceivingOrderDetails
{
    public Guid Id { get; set; }
    public bool Posted { get; set; }
    public DateTime DateTime { get; set; }
    public string? Number { get; set; }
    public string? Comment { get; set; }
    public string? WarehouseName { get; set; }
    public string? ReceivingLocationName { get; set; }
    public ReceivingOrderStatus Status { get; set; }
    public WarehouseOperation WarehouseOperation { get; set; }
    public BusinessOperation BusinessOperation { get; set; }

    public DateTimeOffset? StartedAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public string? SenderName { get; set; }

    public string? BaseOrderType { get; set; }

    public IEnumerable<ReceivingOrderItemDetails> Items { get; set; } = [];

    public bool IsFullyReceived => Items.All(x => x.IsFullyReceived);

    public bool HasPlanFactDifference => Items.Any(x => x.IsPlanFactDifference);

    public static Expression<Func<ReceivingOrder, ReceivingOrderDetails>> Projection =>
        order => new ReceivingOrderDetails
        {
            Id = order.Id,
            DateTime = order.DateTime,
            Number = order.Number,
            Posted = order.Posted,
            WarehouseName = order.Warehouse == null ? string.Empty : order.Warehouse.Name,
            ReceivingLocationName = order.ReceivingLocation == null ? string.Empty : order.ReceivingLocation.Name,
            Status = order.Status,
            BusinessOperation = order.BusinessOperation,
            WarehouseOperation = order.WarehouseOperation,
            SenderName = order.SenderType, // TODO: Get sender name based on SenderType and SenderId
            Comment = order.Comment,
            StartedAtUtc = order.StartedAtUtc,
            CompletedAtUtc = order.CompletedAtUtc,
            Items = order.Items.Select(item => new ReceivingOrderItemDetails
            {
                ReceivingOrderId = item.ReceivingOrderId,
                LineNumber = item.LineNumber,
                StockKeepingUnitName = item.StockKeepingUnit == null ? string.Empty : item.StockKeepingUnit.Name,
                PlanQuantity = item.PlanQuantity,
                FactQuantity = item.FactQuantity,
                Comment = item.Comment
            })
        };
}
