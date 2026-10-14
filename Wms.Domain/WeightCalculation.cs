using Wms.Domain;

namespace Wms.Common;

public static class WeightCalculation
{
    public static double? CalculateKg(decimal quantity, StockKeepingUnit? stockKeepingUnit) =>
        stockKeepingUnit?.WeightKg is double unitWeightKg
            ? (double)quantity * unitWeightKg
            : null;
}
