namespace Wms.Domain;

public static class WeightCalculation
{
    public static double? CalculateKg(double quantity, StockKeepingUnit? stockKeepingUnit) =>
        stockKeepingUnit?.WeightKg is double unitWeightKg
            ? quantity * unitWeightKg
            : null;
}
