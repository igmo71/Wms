namespace Wms.Domain;

public class UnitOfMeasure
{
    public Guid Id { get; set; }
    public string? Name { get; set; }
    public string? Code { get; set; }
    public string? Description { get; set; }
    public string? Abbreviation { get; set; }
    public bool DeletionMark { get; set; }
    public long Numerator { get; set; }
    public long Denominator { get; set; }
}
