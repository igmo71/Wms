using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.Services.Inventory;

public class InventoryTurnoverListItem
{
    public InventoryTurnover Turnover { get; init; } = null!;
    public string? RecorderNumber { get; init; }
    public DateTime? RecorderDate { get; init; }

    public DateTimeOffset CreatedAtUtc => Turnover.CreatedAtUtc;
    public Warehouse? Warehouse => Turnover.Warehouse;
    public StorageLocation? StorageLocation => Turnover.StorageLocation;
    public StockKeepingUnit? StockKeepingUnit => Turnover.StockKeepingUnit;
    public double QuantityDelta => Turnover.QuantityDelta;
    public double BalanceBefore => Turnover.BalanceBefore;
    public double BalanceAfter => Turnover.BalanceAfter;
    public double? WeightDeltaKg => Turnover.WeightDeltaKg;
    public double? WeightBeforeKg => Turnover.WeightBeforeKg;
    public double? WeightAfterKg => Turnover.WeightAfterKg;
    public RecorderType RecorderType => Turnover.InventoryMovement?.RecorderType ?? RecorderType.Unknown;
    public Guid? RecorderId => Turnover.InventoryMovement?.RecorderId;
}
