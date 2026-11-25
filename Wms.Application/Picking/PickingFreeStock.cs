using Wms.Application.Inventory;

namespace Wms.Application.Picking;

public readonly record struct PickingFreeStock(decimal Quantity, bool HasExpiredStock)
{
    internal static PickingFreeStock ForGroup(StockAvailability stock, Guid assemblyId,
        DateOnly today)
    {
        var quantity = stock.AvailableForAssemblyPick(assemblyId);
        return new PickingFreeStock(quantity, quantity > 0
            && stock.Group.ExpirationDate is DateOnly expiry && expiry < today);
    }
}
