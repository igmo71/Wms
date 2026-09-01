using Wms.Contracts.Mobile.V1;

namespace Wms.Mobile;

public sealed class ShippingOrderQueueItemViewState
{
    public required Guid Id { get; init; }
    public required string Number { get; init; }
    public required string DateText { get; init; }
    public required string StatusText { get; init; }
    public required string DetailsText { get; init; }
    public required string ProgressText { get; init; }

    public static ShippingOrderQueueItemViewState ForPicking(
        MobileShippingOrderSummaryResponse order) => new()
    {
        Id = order.Id,
        Number = order.Number,
        DateText = order.Date.ToString("dd.MM.yyyy HH:mm"),
        StatusText = MapStatus(order.Status),
        DetailsText = BuildDetails(order),
        ProgressText = $"Отобрано: {order.Progress.FactQuantity:g} из {order.Progress.PlanQuantity:g} · "
            + $"Строк: {order.Progress.FullyPickedLineCount} из {order.Progress.TotalLineCount}"
    };

    public static ShippingOrderQueueItemViewState ForShipping(
        MobileShippingOrderSummaryResponse order) => new()
    {
        Id = order.Id,
        Number = order.Number,
        DateText = order.Date.ToString("dd.MM.yyyy HH:mm"),
        StatusText = "Готов к отгрузке",
        DetailsText = BuildDetails(order),
        ProgressText = $"К отгрузке: {order.Progress.FactQuantity:g} · "
            + $"Строк: {order.Progress.TotalLineCount}"
    };

    private static string BuildDetails(MobileShippingOrderSummaryResponse order)
    {
        var queue = string.IsNullOrWhiteSpace(order.Queue)
            ? string.Empty
            : $" · {order.Queue}";
        var plannedDate = order.PlannedShippingDate is DateTime date
            ? $"\nПлан отгрузки: {date:dd.MM.yyyy HH:mm}"
            : string.Empty;
        var location = order.ShippingLocation is null
            ? string.Empty
            : $"\nПозиция: {order.ShippingLocation.Address}";
        return $"{order.ReceiverName}{queue}{plannedDate}{location}";
    }

    private static string MapStatus(MobileShippingOrderStatus status) => status switch
    {
        MobileShippingOrderStatus.Prepared => "Подготовлен",
        MobileShippingOrderStatus.ReadyForPicking => "В отборе",
        MobileShippingOrderStatus.ReadyForVerification => "Готов к проверке",
        MobileShippingOrderStatus.InVerification => "На проверке",
        MobileShippingOrderStatus.Verified => "Проверен",
        _ => "Отбор"
    };
}
