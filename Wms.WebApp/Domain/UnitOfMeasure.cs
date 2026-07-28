using Wms.WebApp.Abstractions;

namespace Wms.WebApp.Domain;

public class UnitOfMeasure : EntityBase
{
    public string? Name { get; private set; }
    public string? Code { get; private set; }
    public string? Description { get; private set; }
    public string? Abbreviation { get; private set; }
    public bool DeletionMark { get; private set; }
    public double Numerator { get; set; }
    public double Denominator { get; set; }
}
