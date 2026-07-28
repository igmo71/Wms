using Wms.WebApp.Abstractions;

namespace Wms.WebApp.Domain;

public class StockKeepingUnit : EntityBase
{
    public string? Name { get; private set; }
    public string? Code { get; private set; }
    public string? Description { get; private set; }
    public bool DeletionMark { get; private set; }

    public Guid? BaseUnitOfMeasureId { get; private set; }
    public UnitOfMeasure? BaseUnitOfMeasure { get; private set; }

    public decimal? Weight { get; private set; }
    public decimal? Volume { get; private set; }

    public Guid ParentId { get; set; }
    public StockKeepingUnit? Parent { get; set; }

    public bool IsFolder { get; set; }
}
