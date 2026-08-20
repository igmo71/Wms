namespace Wms.Integration.OneS;

internal static class ODataCleanerExtensions
{
    public static string TrimODataPrefix(this string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        return input.Replace("StandardODATA.", string.Empty);
    }

    public static string TrimPrefixBeforeDot(this string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        int dotIndex = input.IndexOf('.');

        if (dotIndex >= 0 && dotIndex < input.Length - 1)
        {
            return input[(dotIndex + 1)..];
        }

        return input;
    }
}
