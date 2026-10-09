using Wms.Domain;

namespace Wms.Application.StorageLocations;

public sealed class CreateStorageLocationCommand
{
    public required Guid WarehouseId { get; init; }
    public Guid? ZoneId { get; init; }
    public required Guid? ParentId { get; init; }
    public required string Code { get; init; }
    public required StorageLocationDetails Details { get; init; }
}
