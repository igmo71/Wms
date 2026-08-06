namespace Wms.Common;

public class StorageLocationListQuery : ListQuery
{
    public Guid? WarehouseId { get; set; }
    public Guid? ZoneId { get; set; }
}
