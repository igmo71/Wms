namespace Wms.Domain;

public class StockKeepingUnit
{
    public Guid Id { get; set; }
    public string? Name { get; set; }
    public string? Code { get; set; }
    public string? Description { get; set; }
    public bool DeletionMark { get; set; }

    public Guid? BaseUnitOfMeasureId { get; set; }
    public UnitOfMeasure? BaseUnitOfMeasure { get; set; }

    public double? WeightKg { get; set; }

    public Guid? ParentId { get; set; }
    public StockKeepingUnit? Parent { get; set; }

    public bool IsFolder { get; set; }
}
