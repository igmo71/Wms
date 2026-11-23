using Wms.Contracts.Mobile.V1;

namespace Wms.Mobile;

internal static class ShippingQueuePresentation
{
    public static string GetIcon(this MobileShippingOrderQueue queue) => queue switch
    {
        MobileShippingOrderQueue.LiveQueue => "material_people.png",
        MobileShippingOrderQueue.CollectByDate => "material_event.png",
        MobileShippingOrderQueue.OwnDelivery => "material_local_shipping.png",
        _ => "material_help_outline.png"
    };

    public static string GetDisplayText(this MobileShippingOrderQueue queue) => queue switch
    {
        MobileShippingOrderQueue.LiveQueue => "Живая Очередь",
        MobileShippingOrderQueue.CollectByDate => "Собрать К Дате",
        MobileShippingOrderQueue.OwnDelivery => "Собственная Доставка",
        _ => "Очередность не указана"
    };
}
