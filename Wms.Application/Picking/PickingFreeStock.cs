using Wms.Application.Inventory;

namespace Wms.Application.Picking;

public readonly record struct PickingFreeStock(decimal Quantity, bool HasExpiredStock)
{
    internal static PickingFreeStock ForGroup(StockAvailability stock, Guid assemblyId,
        DateOnly today)
    {
        var quantity = stock.AvailableForNewPick(assemblyId);
        return new PickingFreeStock(quantity, quantity > 0
            && stock.Group.ExpirationDate is DateOnly expiry && expiry < today);
    }

    internal static PickingFreeStock Sum(IEnumerable<PickingFreeStock> groups)
    {
        decimal quantity = 0;
        var hasExpiredStock = false;
        foreach (var group in groups)
        {
            quantity += group.Quantity;
            hasExpiredStock |= group.HasExpiredStock;
        }
        return new PickingFreeStock(quantity, hasExpiredStock);
    }
}
