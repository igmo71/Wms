namespace Wms.Common;

public static class WarehouseQuantity
{
    public const int Precision = 15;
    public const int Scale = 3;
    public const decimal Maximum = 999_999_999_999.999m;

    public static bool IsSupported(decimal value) =>
        value >= -Maximum
        && value <= Maximum
        && decimal.Round(value, Scale) == value;

    public static bool IsNonNegative(decimal value) =>
        value >= 0 && IsSupported(value);

    public static bool IsPositive(decimal value) =>
        value > 0 && IsSupported(value);
}
