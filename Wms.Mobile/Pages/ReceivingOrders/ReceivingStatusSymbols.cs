using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Wms.Contracts.Mobile.V1;

namespace Wms.Mobile;

internal enum ReceivingStatusTone { Neutral, Waiting, InProgress, Success, Failure }

internal readonly record struct ReceivingStatusSymbol(
    string Glyph, string Description, ReceivingStatusTone Tone)
{
    public Color TextColor => ReceivingStatusSymbols.ColorFor(Tone);
    public FontAttributes GlyphFontAttributes => ReceivingStatusSymbols.FontForGlyph(Glyph);
}

internal static class ReceivingStatusSymbols
{
    public static ReceivingStatusSymbol ForOrder(MobileReceivingOrderStatus status) => status switch
    {
        MobileReceivingOrderStatus.ReadyForReceiving => new("◇", "К приёмке", ReceivingStatusTone.Waiting),
        MobileReceivingOrderStatus.InReceiving => new("○", "В работе", ReceivingStatusTone.InProgress),
        MobileReceivingOrderStatus.Received => new("✓", "Принят", ReceivingStatusTone.Success),
        _ => new("?", status.ToString(), ReceivingStatusTone.Neutral)
    };

    public static ReceivingStatusSymbol ForLpn(MobileLicensePlateNumberStatus status) => status switch
    {
        MobileLicensePlateNumberStatus.Available => new("◇", "Доступен", ReceivingStatusTone.Waiting),
        MobileLicensePlateNumberStatus.Open => new("○", "Открыт", ReceivingStatusTone.InProgress),
        MobileLicensePlateNumberStatus.Closed => new("✓", "Закрыт: приход проведён", ReceivingStatusTone.Success),
        MobileLicensePlateNumberStatus.Cancelled => new("⊘", "Отменён", ReceivingStatusTone.Failure),
        MobileLicensePlateNumberStatus.Deleted => new("×", "Удалён", ReceivingStatusTone.Failure),
        _ => new("?", status.ToString(), ReceivingStatusTone.Neutral)
    };

    public static ReceivingStatusSymbol ForSku(decimal? plan, decimal? fact)
    {
        if (fact is null) return new("?", "не проверен", ReceivingStatusTone.Waiting);
        if (plan is null) return new(string.Empty, "план неизвестен", ReceivingStatusTone.Neutral);
        return plan == fact
            ? new("✓", "совпадает с планом", ReceivingStatusTone.Success)
            : new("≠", "расходится с планом", ReceivingStatusTone.Failure);
    }

    public static ReceivingStatusSymbol ForDocumentCheck(bool hasError) => hasError
        ? new("⚠", "Ошибка последней сверки документа 1С", ReceivingStatusTone.Failure)
        : new(string.Empty, string.Empty, ReceivingStatusTone.Neutral);

    public static Color FactColor(decimal? plan, decimal? fact) =>
        ForSku(plan, fact).TextColor;

    internal static Color ColorFor(ReceivingStatusTone tone)
    {
        var dark = Application.Current?.RequestedTheme == AppTheme.Dark;
        return tone switch
        {
            ReceivingStatusTone.Waiting => Color.FromArgb(dark ? "#93C5FD" : "#1D4ED8"),
            ReceivingStatusTone.InProgress => Color.FromArgb(dark ? "#FFD54F" : "#8A5900"),
            ReceivingStatusTone.Success => Color.FromArgb(dark ? "#87DFA2" : "#176B34"),
            ReceivingStatusTone.Failure => Color.FromArgb(dark ? "#FF8A80" : "#B3261E"),
            _ => dark ? Colors.White : Colors.Black
        };
    }

    internal static FontAttributes FontForGlyph(string glyph) =>
        glyph is "\u25c7" or "\u25cb" or "\u2298"
            ? FontAttributes.Bold : FontAttributes.None;

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
