using System.Text.Json;

namespace Wms.WebApp.Integration.OneS;

public class OneCClient(HttpClient httpClient, ILogger<OneCClient> logger)
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly ILogger<OneCClient> _logger = logger;

    public async Task<TValue?> GetValueAsync<TValue>(string? uri, CancellationToken ct = default)
    {
        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("{Source} - Start {Uri}", nameof(GetValueAsync), uri);

        using var response = await _httpClient.GetAsync(uri, ct);

        var content = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            var error = JsonSerializer.Deserialize<OneCError>(content);
            if (_logger.IsEnabled(LogLevel.Error))
                _logger.LogError("{Source} {Uri} {@Error} {content}", nameof(GetValueAsync), uri, error, content);
            return default;
        }

        var result = JsonSerializer.Deserialize<TValue>(content);

        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("{Source} - Ok {Uri} {@Result}", nameof(GetValueAsync), uri, result);

        return result;
    }
}
