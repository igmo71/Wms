namespace Wms.Domain;

public class InventoryBalance
{
    public Guid Id { get; set; }

    public Guid WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }

    public Guid StorageLocationId { get; set; }
    public StorageLocation? StorageLocation { get; set; }

    public Guid StockKeepingUnitId { get; set; }
    public StockKeepingUnit? StockKeepingUnit { get; set; }

    public double Quantity { get; set; }
    public double? WeightKg => WeightCalculation.CalculateKg(Quantity, StockKeepingUnit);

    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? UpdatedAtUtc { get; set; }

    public byte[] RowVersion { get; set; } = [];
}
