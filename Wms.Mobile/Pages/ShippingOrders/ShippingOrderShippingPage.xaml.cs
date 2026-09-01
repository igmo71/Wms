using Wms.Contracts.Mobile.V1;

namespace Wms.Mobile;

public partial class ShippingOrderShippingPage : ContentPage
{
    public ShippingOrderShippingPage()
    {
        InitializeComponent();
    }

    public IReadOnlyList<MobileShippingOrderLineResponse> Lines { get; private set; } = [];

    public void Show(MobileShippingOrderDetailsResponse details)
    {
        Lines = details.Lines;
        OnPropertyChanged(nameof(Lines));
        NumberLabel.Text = $"Ордер {details.Order.Number}";
        WarehouseLabel.Text = $"Склад: {details.Order.WarehouseName}";
        ReceiverLabel.Text = $"Получатель: {details.Order.ReceiverName}";
        LocationLabel.Text = details.Order.ShippingLocation is null
            ? "Позиция отгрузки не указана"
            : $"Позиция отгрузки: {details.Order.ShippingLocation.Address}";
        ProgressLabel.Text = $"К отгрузке: {details.Order.Progress.FactQuantity:g}";
    }
}
