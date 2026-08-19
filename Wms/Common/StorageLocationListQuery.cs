namespace Wms.Common;

public class StorageLocationListQuery : ListQuery
{
    public bool ExcludeFolders { get; set; } = true;
    public Guid? WarehouseId { get; set; }
    public Guid? ZoneId { get; set; }
    public Domain.Enums.ZoneType? ZoneType { get; set; }
}
