namespace Wms.Mobile.Services;

internal static class WarehouseQuantityInput
{
    private const int Scale = 3;
    private const decimal Maximum = 999_999_999_999.999m;

    public static bool IsSupported(decimal value) =>
        value >= 0
        && value <= Maximum
        && decimal.Round(value, Scale) == value;
}
