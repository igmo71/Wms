using Wms.Domain.Enums;

namespace Wms.Application.Picking;

public static class ShippingQueuePriority
{
    public static int Rank(ShippingOrderQueue queue) => queue switch
    {
        ShippingOrderQueue.LiveQueue => 0,
        ShippingOrderQueue.CollectByDate => 1,
        ShippingOrderQueue.OwnDelivery => 2,
        _ => 3
    };
}
