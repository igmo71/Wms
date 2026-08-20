using Wms.Common;
using Wms.Domain.Enums;

namespace Wms.Application.Zones;

public sealed class ZoneListQuery : ListQuery
{
    public Guid? WarehouseId { get; set; }
    public ZoneType? Type { get; set; }
}
