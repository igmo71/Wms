using MudBlazor;
using Wms.Domain.Enums;

namespace Wms.WebApp;

public static class DocumentStatusExtensions
{
    public static Color GetChipColor(this ReceivingOrderStatus status) => status switch
    {
        ReceivingOrderStatus.InReceiving => Color.Primary,
        ReceivingOrderStatus.Received => Color.Success,
        _ => Color.Default
    };

    public static Color GetChipColor(this ShippingOrderStatus status) => status switch
    {
        ShippingOrderStatus.ReadyForPicking => Color.Info,
        ShippingOrderStatus.ReadyForShipment => Color.Primary,
        ShippingOrderStatus.Shipped => Color.Success,
        _ => Color.Default
    };

    public static Color GetChipColor(this InventoryCountStatus status) => status switch
    {
        InventoryCountStatus.Posted => Color.Success,
        _ => Color.Default
    };

    public static Color GetChipColor(this InventoryTransferStatus status) => status switch
    {
        InventoryTransferStatus.InProgress => Color.Primary,
        InventoryTransferStatus.Completed => Color.Success,
        _ => Color.Default
    };
}
