using System.Reflection;
using System.Text.Json;

namespace Wms.Mobile.Services;

internal sealed record MobileApiSettings(
    string DebugBaseAddress,
    string StandaloneBaseAddress)
{
    public string BaseAddress
    {
        get
        {
#if DEBUG
            return DebugBaseAddress;
#else
            return StandaloneBaseAddress;
#endif
        }
    }

    public static MobileApiSettings Load()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("Wms.Mobile.mobile-api.json")
            ?? throw new InvalidOperationException("Конфигурация mobile-api.json не найдена.");
        var settings = JsonSerializer.Deserialize<MobileApiSettings>(stream);

        if (settings is null)
        {
            throw new InvalidOperationException(
                "Конфигурация Resources/Raw/mobile-api.json некорректна.");
        }

        return settings with
        {
            DebugBaseAddress = NormalizeHttpsAddress(
                settings.DebugBaseAddress,
                nameof(DebugBaseAddress)),
            StandaloneBaseAddress = NormalizeHttpsAddress(
                settings.StandaloneBaseAddress,
                nameof(StandaloneBaseAddress))
        };
    }

    private static string NormalizeHttpsAddress(string value, string propertyName)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || string.IsNullOrWhiteSpace(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || uri.AbsolutePath is not ("" or "/"))
        {
            throw new InvalidOperationException(
                $"Resources/Raw/mobile-api.json должен содержать абсолютный HTTPS {propertyName} без пути и параметров.");
        }

        return new UriBuilder(uri)
        {
            Path = "/",
            Query = string.Empty,
            Fragment = string.Empty
        }.Uri.AbsoluteUri;
    }
}
