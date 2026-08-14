namespace Wms.WebApp;

public static class WeightDisplay
{
    public static string Format(double? weightKg) =>
        weightKg is double value ? $"{value:0.###} кг" : "—";

    public static string FormatTotal(double knownWeightKg, bool isComplete) =>
        isComplete ? Format(knownWeightKg) : $"{Format(knownWeightKg)} (неполный)";
}
