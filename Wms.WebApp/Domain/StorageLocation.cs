using Wms.WebApp.Abstractions;

namespace Wms.WebApp.Domain;

public class StorageLocation : EntityBase
{
    public string? Name { get; set; }
    public bool DeletionMark { get; set; }

    public Guid? WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }

    public Guid? ZoneId { get; set; }
    public Zone? Zone { get; set; }
}
