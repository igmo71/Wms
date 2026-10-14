namespace Wms.Domain;

public class StockKeepingUnit
{
    private readonly List<SkuBarcode> _barcodes = [];

    public Guid Id { get; set; }
    public string? Name { get; set; }
    public string? Code { get; set; }
    public bool DeletionMark { get; set; }

    public Guid? BaseUnitOfMeasureId { get; set; }
    public UnitOfMeasure? BaseUnitOfMeasure { get; set; }

    public double? WeightKg { get; set; }
    public double? VolumeM3 { get; set; }

    public IReadOnlyCollection<SkuBarcode> Barcodes => _barcodes;
}
