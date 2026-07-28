using Wms.WebApp.Abstractions;

namespace Wms.WebApp.Domain;

public class UnitOfMeasure : EntityBase
{
    public string? Name { get; private set; }
    public string? Code { get; private set; }

    public string? Symbol { get; private set; }

    public bool DeletionMark { get; private set; }
}
