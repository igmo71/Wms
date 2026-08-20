using Wms.Common;
using Wms.Domain;

namespace Wms.Application.Inventory.Transfers;

public class InventoryTransferTransitBalance
{
    public StockKeepingUnit StockKeepingUnit { get; init; } = null!;
    public double Quantity { get; init; }
    public double? WeightKg => WeightCalculation.CalculateKg(Quantity, StockKeepingUnit);
}
