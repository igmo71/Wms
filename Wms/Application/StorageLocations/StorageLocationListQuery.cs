using Wms.Common;
using Wms.Domain.Enums;

namespace Wms.Application.StorageLocations;

public sealed class StorageLocationListQuery : ListQuery
{
    public bool ExcludeFolders { get; set; } = true;
    public Guid? WarehouseId { get; set; }
    public Guid? ZoneId { get; set; }
    public ZoneType? ZoneType { get; set; }
}
