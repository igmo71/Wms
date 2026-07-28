using Wms.WebApp.Abstractions;

namespace Wms.WebApp.Domain;

public class StockKeepingUnit : EntityBase
{
    public string Code { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public string? Description { get; private set; }

    public Guid BaseUnitOfMeasureId { get; private set; }
    public UnitOfMeasure BaseUnitOfMeasure { get; private set; } = null!;

    public bool DeletionMark { get; private set; } = true;

    public decimal? Weight { get; private set; }
    public decimal? Volume { get; private set; }
}
