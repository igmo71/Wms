using Wms.Common;
using Wms.Domain;

namespace Wms.Application.ShippingOrders;

public sealed class PickingSourceLocationAvailability
{
    public required StorageLocation StorageLocation { get; init; }
    public required StockKeepingUnit StockKeepingUnit { get; init; }
    public double PhysicalQuantity { get; init; }
    public double DraftQuantity { get; init; }
    public double? PhysicalWeightKg => WeightCalculation.CalculateKg(PhysicalQuantity, StockKeepingUnit);
    public double? AvailableWeightKg => WeightCalculation.CalculateKg(
        Math.Max(0, PhysicalQuantity - DraftQuantity), StockKeepingUnit);
}
