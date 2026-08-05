namespace Wms.Domain;

public class StockKeepingUnit
{
    public Guid Id { get; set; }
    public string? Name { get; set; }
    public string? Code { get; set; }
    public bool DeletionMark { get; set; }

    public Guid? BaseUnitOfMeasureId { get; set; }
    public UnitOfMeasure? BaseUnitOfMeasure { get; set; }

    public double? WeightKg { get; set; }

    public List<SkuBarcode>? Barcodes { get; set; }
}
