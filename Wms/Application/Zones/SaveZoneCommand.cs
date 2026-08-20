using Wms.Domain.Enums;

namespace Wms.Application.Zones;

public sealed class SaveZoneCommand
{
    public Guid? Id { get; init; }
    public required Guid WarehouseId { get; init; }
    public required string Code { get; init; }
    public required string Name { get; init; }
    public required ZoneType Type { get; init; }
}
