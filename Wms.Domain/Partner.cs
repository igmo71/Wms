namespace Wms.Domain;

public class Partner
{
    public Guid Id { get; set; }
    public string? Name { get; set; }
    public string? Code { get; set; }
    public bool DeletionMark { get; set; }
    public Guid? ParentId { get; set; }
}
