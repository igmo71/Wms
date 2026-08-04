using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Wms.Integration.OneS;

public class OneCClient(HttpClient httpClient, ILogger<OneCClient> logger)
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly ILogger<OneCClient> _logger = logger;

    public async Task<TValue?> GetValueAsync<TValue>(string uri, CancellationToken ct = default)
    {
        var source = nameof(GetValueAsync);

        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("{Source} - Start {Uri}", source, uri);

        using var response = await _httpClient.GetAsync(uri, ct);

        var responseContent = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            TryReadError(uri, response, responseContent, source);

            return default;
        }

        var result = await response.Content.ReadFromJsonAsync<TValue>(ct);

        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("{Source} - Ok {Uri}", source, uri);

        return result;
    }

    public async Task<TResponse?> PatchValueAsync<TRequest, TResponse>(string uri, TRequest request, CancellationToken ct = default)
    {
        var source = nameof(PatchValueAsync);

        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("{Source} - Start {Uri}", source, uri);

        var json = JsonSerializer.Serialize(request);

        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await _httpClient.PatchAsync(uri, content, ct);

        var responseContent = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            TryReadError(uri, response, responseContent, source);

            return default;
        }

        var result = await response.Content.ReadFromJsonAsync<TResponse>(ct);

        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("{Source} - Ok {Uri} {@result}", source, uri, result);

        return result;
    }

    public async Task<bool> PostValueAsync(string uri, CancellationToken ct = default)
    {
        var source = nameof(PostValueAsync);

        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("{Source} - Start {Uri}", source, uri);

        using var response = await _httpClient.PostAsync(uri, null, ct);

        var responseContent = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            TryReadError(uri, response, responseContent, source);

            return false;
        }

        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("{Source} - Ok {Uri}", source, uri);

        return true;
    }

    private void TryReadError(string uri, HttpResponseMessage response, string content, string source)
    {
        try
        {
            var error = JsonSerializer.Deserialize<OneCError>(content);

            if (_logger.IsEnabled(LogLevel.Error))
                _logger.LogError("{Source} {Uri} StatusCode{} {@Error}", source, uri, response.StatusCode, error);
        }
        catch (Exception)
        {
            if (_logger.IsEnabled(LogLevel.Error))
                _logger.LogError("{Source} {Uri} {StatusCode} {ErrorContent}", source, uri, response.StatusCode, content);
        }
    }
}
