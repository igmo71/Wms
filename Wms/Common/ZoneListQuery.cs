namespace Wms.Common;

public class ZoneListQuery : ListQuery
{
    public Guid? WarehouseId { get; set; }
    public Domain.Enums.ZoneType? Type { get; set; }
}
