using MudBlazor;
using Wms.Domain.Enums;

namespace Wms.WebApp;

internal static class DocumentStatusExtensions
{
    public static string GetIcon(this ReceivingOrderQueue queue) => queue switch
    {
        ReceivingOrderQueue.ForClient => Icons.Material.Filled.Person,
        ReceivingOrderQueue.UrgentlyOnSale => Icons.Material.Filled.Bolt,
        ReceivingOrderQueue.Expired => Icons.Material.Filled.EventBusy,
        _ => Icons.Material.Filled.HelpOutline
    };

    public static string GetIcon(this ShippingOrderQueue queue) => queue switch
    {
        ShippingOrderQueue.LiveQueue => Icons.Material.Filled.People,
        ShippingOrderQueue.CollectByDate => Icons.Material.Filled.Event,
        ShippingOrderQueue.OwnDelivery => Icons.Material.Filled.LocalShipping,
        _ => Icons.Material.Filled.HelpOutline
    };

    public static Color GetChipColor(this ReceivingOrderStatus status) => status switch
    {
        ReceivingOrderStatus.InReceiving => Color.Primary,
        ReceivingOrderStatus.Received => Color.Success,
        _ => Color.Default
    };

    public static Color GetChipColor(this PutawayStatus status) => status switch
    {
        PutawayStatus.Pending => Color.Info,
        PutawayStatus.InProgress => Color.Primary,
        PutawayStatus.Completed => Color.Success,
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
