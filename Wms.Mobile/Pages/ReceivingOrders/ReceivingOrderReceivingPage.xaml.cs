using Wms.Contracts.Mobile.V1;

namespace Wms.Mobile;

public partial class ReceivingOrderReceivingPage : ContentPage
{
    public ReceivingOrderReceivingPage()
    {
        InitializeComponent();
    }

    public void Show(MobileReceivingOrderDetailsResponse details)
    {
        BindingContext = details;
        StatusLabel.Text = details.Order.Status switch
        {
            MobileReceivingOrderStatus.ReadyForReceiving => "Готов к приёмке",
            MobileReceivingOrderStatus.InReceiving => "В приёмке",
            MobileReceivingOrderStatus.ProcessingRequired => "Требуется обработка",
            _ => "Приёмка"
        };
        LocationLabel.Text = details.Order.ReceivingLocation is null
            ? "Позиция приёмки не выбрана"
            : $"Позиция приёмки: {details.Order.ReceivingLocation.Address}";
        ProgressLabel.Text = $"Факт: {details.Order.Progress.FactQuantity:g} из "
            + $"{details.Order.Progress.PlanQuantity:g} · Проверено строк: "
            + $"{details.Order.Progress.ConfirmedLineCount} из "
            + $"{details.Order.Progress.TotalLineCount}";
    }
}
