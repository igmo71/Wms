namespace Wms.Mobile;

public readonly record struct PickingStockSummary(decimal RouteGap, decimal? FreeQuantity)
{
    private decimal Shortage => FreeQuantity is decimal free ? Math.Max(0, RouteGap - free) : 0;

    public bool HasFreeStock => RouteGap > 0 && FreeQuantity is > 0;
    public string FreeStockText => $"Свободный остаток {FreeQuantity:0.###}";
    public bool HasShortage => Shortage > 0;
    public string ShortageText => $"Не хватает {Shortage:0.###}";
}
