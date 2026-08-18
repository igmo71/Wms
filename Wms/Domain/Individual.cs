namespace Wms.Domain;

public class Individual
{
    public Guid Id { get; set; }
    public string? Name { get; set; }
    public bool DeletionMark { get; set; }
    public Guid? ParentId { get; set; }
    public bool IsFolder { get; set; }
}
