namespace Wms.Domain;

public class DeliveryDirection
{
    public Guid Id { get; set; }
    public bool DeletionMark { get; set; }
    public Guid? ParentId { get; set; }
    public bool IsFolder { get; set; }
    public string? Description { get; set; }
    public string? Comment { get; set; }
}
