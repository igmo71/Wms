namespace Wms.Domain;

public class Warehouse
{
    public Guid Id { get; set; }
    public string? Name { get; set; }
    public bool DeletionMark { get; set; }

    public List<Zone>? Zones { get; set; }

    public List<StorageLocation>? StorageLocations { get; set; }
}
