using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Networking;
using Wms.Contracts.Mobile.V1;

namespace Wms.Mobile.Services;

internal sealed class MobileConnectionDiagnostics(MobileApiSettings settings)
{
    private string? _lastApplicationFailure;

    public void RecordFailure(string operation, HttpRequestException exception)
    {
        var details = new StringBuilder();
        details.AppendLine($"Время: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        details.AppendLine($"Операция: {operation}");
        AppendException(details, exception);
        _lastApplicationFailure = details.ToString().TrimEnd();
    }

    public async Task<string> RunAsync()
    {
        var report = new StringBuilder();
        var apiUri = new Uri(new Uri(settings.BaseAddress), MobileApiRoutes.Me);

        report.AppendLine($"Время: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        report.AppendLine($"Устройство: {DeviceInfo.Current.Manufacturer} {DeviceInfo.Current.Model}");
        report.AppendLine($"Android: {DeviceInfo.Current.VersionString}");
        report.AppendLine($"Адрес API: {apiUri}");

        if (_lastApplicationFailure is not null)
        {
            report.AppendLine();
            report.AppendLine("Последняя ошибка приложения:");
            report.AppendLine(_lastApplicationFailure);
        }

        AppendNetworkDetails(report);

        report.AppendLine();
        report.AppendLine("Разрешение имени:");
        using (var dnsTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(8)))
        {
            try
            {
                var addresses = await Dns.GetHostAddressesAsync(apiUri.Host, dnsTimeout.Token);
                report.AppendLine(addresses.Length == 0
                    ? "  DNS не вернул IP-адресов."
                    : $"  IP-адреса: {string.Join(", ", addresses.Select(address => address.ToString()))}");
            }
            catch (Exception exception)
            {
                AppendException(report, exception);
            }
        }

        report.AppendLine();
        report.AppendLine("HTTPS-запрос без учётных данных:");
        try
        {
            using var handler = MobileAuthenticationHandler.CreateInnerHandler();
            handler.AllowAutoRedirect = false;
            using var client = new HttpClient(handler, disposeHandler: false)
            {
                Timeout = TimeSpan.FromSeconds(12)
            };
            using var response = await client.GetAsync(
                apiUri,
                HttpCompletionOption.ResponseHeadersRead);
            report.AppendLine($"  HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                report.AppendLine("  API доступен; ответ 401 ожидаем без входа.");
            }
        }
        catch (Exception exception)
        {
            AppendException(report, exception);
        }

        return report.ToString().TrimEnd();
    }

    private static void AppendNetworkDetails(StringBuilder report)
    {
        report.AppendLine();
        report.AppendLine("Сеть устройства:");

        try
        {
            report.AppendLine($"  Доступ: {Connectivity.Current.NetworkAccess}");
            report.AppendLine($"  Подключения: {string.Join(", ", Connectivity.Current.ConnectionProfiles)}");

#if ANDROID
            var manager = Android.App.Application.Context.GetSystemService(
                Android.Content.Context.ConnectivityService) as Android.Net.ConnectivityManager;
            var network = manager?.ActiveNetwork;
            var properties = network is null ? null : manager?.GetLinkProperties(network);
            if (properties is not null)
            {
                report.AppendLine($"  Активный интерфейс: {properties.InterfaceName ?? "не указан"}");
                report.AppendLine($"  Адреса устройства: {string.Join(", ", properties.LinkAddresses.Select(address => address.ToString()))}");
                report.AppendLine($"  DNS-серверы: {string.Join(", ", properties.DnsServers.Select(address => address.HostAddress))}");
                report.AppendLine($"  Поисковый домен: {properties.Domains ?? "не задан"}");
                if ((int)Android.OS.Build.VERSION.SdkInt >= 28)
                {
                    report.AppendLine($"  Частный DNS активен: {properties.IsPrivateDnsActive}");
                    report.AppendLine($"  Провайдер частного DNS: {properties.PrivateDnsServerName ?? "не задан"}");
                }
            }
            else
            {
                report.AppendLine("  Параметры активной сети недоступны.");
            }
#endif
        }
        catch (Exception exception)
        {
            AppendException(report, exception);
        }
    }

    private static void AppendException(StringBuilder report, Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            report.AppendLine($"  {current.GetType().FullName}: {current.Message}");
            report.AppendLine($"  HResult: 0x{current.HResult:X8}");
            if (current is HttpRequestException httpException)
            {
                report.AppendLine($"  HTTP-ошибка: {httpException.HttpRequestError}");
                if (httpException.StatusCode is { } statusCode)
                {
                    report.AppendLine($"  HTTP-статус: {(int)statusCode}");
                }
            }

            if (current is SocketException socketException)
            {
                report.AppendLine($"  Ошибка сокета: {socketException.SocketErrorCode}");
            }
        }
    }
}
