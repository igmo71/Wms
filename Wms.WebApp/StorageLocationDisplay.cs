using Wms.Domain;

namespace Wms.WebApp;

internal static class StorageLocationDisplay
{
    public static string Format(StorageLocation? location)
    {
        if (location is null)
        {
            return string.Empty;
        }

        return location.Zone is null
            ? location.Code
            : $"{location.Zone.Code}-{location.Code}";
    }

    public static string FormatOrDash(StorageLocation? location) =>
        location is null ? "—" : Format(location);
}
