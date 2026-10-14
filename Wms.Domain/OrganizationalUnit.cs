namespace Wms.Domain;

public class OrganizationalUnit
{
    public Guid Id { get; set; }
    public string? Code { get; set; }
    public string? Name { get; set; }
    public bool DeletionMark { get; set; }
    public Guid? ParentId { get; set; }
}
