using Wms.Domain;

namespace Wms.Application.Services.Inventory;

public class InventoryTransferTransitBalance
{
    public StockKeepingUnit StockKeepingUnit { get; init; } = null!;
    public double Quantity { get; init; }
}
