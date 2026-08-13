namespace Wms.Domain;

public class Zone
{
    public Guid Id { get; set; }
    public string? Name { get; set; }
    public bool DeletionMark { get; set; }
    public Enums.ZoneType Type { get; set; }

    public Guid WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }

    public List<StorageLocation>? StorageLocations { get; set; }
}
