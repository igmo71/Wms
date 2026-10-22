using Wms.Contracts.Mobile.V1;

namespace Wms.Mobile;

internal readonly record struct ReceivingStatusSymbol(string Glyph, string Description);

internal static class ReceivingStatusSymbols
{
    public static ReceivingStatusSymbol ForOrder(MobileReceivingOrderStatus status) => status switch
    {
        MobileReceivingOrderStatus.ReadyForReceiving => new("◇", "К приёмке"),
        MobileReceivingOrderStatus.InReceiving => new("○", "В работе"),
        MobileReceivingOrderStatus.Received => new("✓", "Принят"),
        _ => new("?", status.ToString())
    };

    public static ReceivingStatusSymbol ForLpn(MobileLicensePlateNumberStatus status) => status switch
    {
        MobileLicensePlateNumberStatus.Available => new("◇", "Доступен"),
        MobileLicensePlateNumberStatus.Open => new("○", "Открыт"),
        MobileLicensePlateNumberStatus.Closed => new("✓", "Закрыт: приход проведён"),
        MobileLicensePlateNumberStatus.Cancelled => new("⊘", "Отменён"),
        MobileLicensePlateNumberStatus.Deleted => new("×", "Удалён"),
        _ => new("?", status.ToString())
    };

    public static ReceivingStatusSymbol ForSku(decimal? plan, decimal? fact)
    {
        if (fact is null) return new("?", "не проверен");
        if (plan is null) return new(string.Empty, "план неизвестен");
        return plan == fact
            ? new("✓", "совпадает с планом")
            : new("≠", "расходится с планом");
    }

    public static ReceivingStatusSymbol ForDocumentCheck(bool hasError) => hasError
        ? new("⚠", "Ошибка последней сверки документа 1С")
        : new(string.Empty, string.Empty);

    public static string FactText(decimal? plan, decimal? fact)
    {
        if (fact is null) return "?";
        var symbol = ForSku(plan, fact).Glyph;
        return string.IsNullOrEmpty(symbol) ? $"{fact:0.###}" : $"{symbol} {fact:0.###}";
    }

    public static string FactDescription(decimal? plan, decimal? fact) => fact is null
        ? "Факт не проверен"
        : $"Факт {fact:0.###}, {ForSku(plan, fact).Description}";
}
