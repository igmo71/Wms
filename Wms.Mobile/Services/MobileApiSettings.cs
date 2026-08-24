using System.Reflection;
using System.Text.Json;

namespace Wms.Mobile.Services;

internal sealed record MobileApiSettings(string BaseAddress)
{
    public static MobileApiSettings Load()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("Wms.Mobile.mobile-api.json")
            ?? throw new InvalidOperationException("Конфигурация mobile-api.json не найдена.");
        var settings = JsonSerializer.Deserialize<MobileApiSettings>(stream);

        if (settings is null
            || !Uri.TryCreate(settings.BaseAddress, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                "Resources/Raw/mobile-api.json должен содержать абсолютный HTTPS BaseAddress.");
        }

        return settings with { BaseAddress = uri.ToString() };
    }
}
