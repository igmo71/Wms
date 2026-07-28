using Wms.WebApp.Abstractions;

namespace Wms.WebApp.Domain;

public class Warehouse : EntityBase
{
    public string? Name { get; set; }
    public bool DeletionMark { get; set; }

    public List<Zone>? Zones { get; set; }

    public List<StorageLocation>? StorageLocations { get; set; }
}
