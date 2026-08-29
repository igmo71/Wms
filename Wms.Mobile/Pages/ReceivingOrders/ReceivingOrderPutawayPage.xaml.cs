using Wms.Contracts.Mobile.V1;

namespace Wms.Mobile;

public partial class ReceivingOrderPutawayPage : ContentPage
{
    public ReceivingOrderPutawayPage()
    {
        InitializeComponent();
    }

    public void Show(MobileReceivingOrderDetailsResponse details)
    {
        BindingContext = details;
        StatusLabel.Text = details.Order.PutawayStatus == MobilePutawayStatus.Pending
            ? "Ожидает размещения"
            : "В размещении";
        LocationLabel.Text = details.Order.ReceivingLocation is null
            ? "Позиция приёмки не указана"
            : $"Позиция приёмки: {details.Order.ReceivingLocation.Address}";
        ProgressLabel.Text = $"Размещено: {details.Order.Progress.AllocatedQuantity:g} из "
            + $"{details.Order.Progress.FactQuantity:g} · Строк: "
            + $"{details.Order.Progress.FullyAllocatedLineCount} из "
            + $"{details.Order.Progress.PositiveLineCount}";
        BindableLayout.SetItemsSource(
            LinesLayout,
            details.Lines.Where(x => x.FactQuantity > 0).ToList());
    }
}
