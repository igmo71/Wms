using Microsoft.Extensions.Logging;
using System.Net;
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
        using var response = await _httpClient.GetAsync(uri, ct);

        var responseContent = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            TryReadError(uri, response.StatusCode, responseContent, nameof(GetValueAsync));

            return default;
        }

        var result = await response.Content.ReadFromJsonAsync<TValue>(ct);

        return result;
    }

    public async Task<TResponse?> PatchValueAsync<TRequest, TResponse>(string uri, TRequest request, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(request);

        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await _httpClient.PatchAsync(uri, content, ct);

        var responseContent = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            TryReadError(uri, response.StatusCode, responseContent, nameof(PatchValueAsync));

            return default;
        }

        var result = await response.Content.ReadFromJsonAsync<TResponse>(ct);

        return result;
    }

    public async Task<bool> PostValueAsync(string uri, CancellationToken ct = default)
    {
        using var response = await _httpClient.PostAsync(uri, null, ct);

        var responseContent = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            TryReadError(uri, response.StatusCode, responseContent, nameof(PostValueAsync));

            return false;
        }

        return true;
    }

    private void TryReadError(string uri, HttpStatusCode httpStatusCode, string content, string source)
    {
        try
        {
            var error = JsonSerializer.Deserialize<OneCError>(content);

            _logger.LogError("{Source} {Uri} {StatusCode} {@Error}", source, uri, httpStatusCode, error);
        }
        catch (Exception)
        {
            _logger.LogError("{Source} {Uri} {StatusCode} {ErrorContent}", source, uri, httpStatusCode, content);
        }
    }
}
