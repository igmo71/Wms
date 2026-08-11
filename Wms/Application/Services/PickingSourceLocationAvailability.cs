using Wms.Domain;

namespace Wms.Application.Services;

public sealed class PickingSourceLocationAvailability
{
    public required StorageLocation StorageLocation { get; init; }
    public double PhysicalQuantity { get; init; }
    public double DraftQuantity { get; init; }
}
