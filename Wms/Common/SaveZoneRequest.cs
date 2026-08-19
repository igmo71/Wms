using Wms.Domain.Enums;

namespace Wms.Common;

public class SaveZoneRequest
{
    public Guid? Id { get; set; }
    public Guid WarehouseId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public ZoneType Type { get; set; }
}
