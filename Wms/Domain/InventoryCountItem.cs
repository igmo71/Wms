namespace Wms.Domain;

public class InventoryCountItem
{
    public Guid Id { get; set; }

    public Guid InventoryCountId { get; set; }
    public InventoryCount? InventoryCount { get; set; }

    public int LineNumber { get; set; }

    public Guid? StorageLocationId { get; set; }
    public StorageLocation? StorageLocation { get; set; }

    public Guid? StockKeepingUnitId { get; set; }
    public StockKeepingUnit? StockKeepingUnit { get; set; }

    public double ExpectedQuantity { get; set; }
    public double CountedQuantity { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public string? UpdatedBy { get; set; }

    public double DifferenceQuantity => CountedQuantity - ExpectedQuantity;
    public double? CountedWeightKg => WeightCalculation.CalculateKg(CountedQuantity, StockKeepingUnit);
}
