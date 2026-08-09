using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using Wms.Common;

namespace Wms.Integration.OneS;

public class OneCClient(HttpClient httpClient, ILogger<OneCClient> logger)
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly ILogger<OneCClient> _logger = logger;

    //private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);


    public Task<ServiceResult<TResponse?>> GetValueAsync<TResponse>(
        string uri,
        CancellationToken ct = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);

        return SendAsync<TResponse>(request, ct);
    }

    public Task<ServiceResult<TResponse?>> PatchValueAsync<TRequest, TResponse>(
        string uri,
        TRequest value,
        CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(value);

        var request = new HttpRequestMessage(HttpMethod.Patch, uri)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        return SendAsync<TResponse>(request, ct);
    }

    public Task<ServiceResult<TResponse?>> PostValueAsync<TRequest, TResponse>(
        string uri,
        TRequest value,
        CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(value);

        var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        return SendAsync<TResponse>(request, ct);
    }

    private async Task<ServiceResult<TResponse?>> SendAsync<TResponse>(HttpRequestMessage request, CancellationToken ct)
    {
        using (request)
        {
            using var scope = _logger.BeginScope("OneCClient {Method} {Uri}", request.Method, request.RequestUri);

            string responseContent = string.Empty;

            try
            {
                using var response = await _httpClient.SendAsync(request, ct);

                responseContent = await response.Content.ReadAsStringAsync(ct);

                if (!response.IsSuccessStatusCode)
                {
                    var error = JsonSerializer.Deserialize<OneCError>(responseContent);

                    _logger.LogError("1C request failed {StatusCode}: {Error}", response.StatusCode, error);

                    return ServiceError.Failure<TResponse>($"1C request failed {response.StatusCode}: {error?.OdataError?.Message?.Value}");
                }

                if (string.IsNullOrWhiteSpace(responseContent))
                    return default(TResponse);

                var result = JsonSerializer.Deserialize<TResponse>(responseContent);

                return result;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "1C returned an invalid JSON response {Method} {Uri} {responseContent}", request.Method, request.RequestUri, responseContent);

                return ServiceError.Failure<TResponse>("1C returned an invalid JSON response");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "1C request failed: {Method} {Uri}", request.Method, request.RequestUri);

                return ServiceError.Failure<TResponse>(ex.Message);
            }
        }

    }

    public Task<ServiceResult> PostValueAsync(string uri, CancellationToken ct = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, uri);

        return SendAsync(request, ct);
    }

    private async Task<ServiceResult> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        using (request)
        {
            using var scope = _logger.BeginScope("OneCClient {Method} {Uri}", request.Method, request.RequestUri);

            string responseContent = string.Empty;

            try
            {
                using var response = await _httpClient.SendAsync(request, ct);

                responseContent = await response.Content.ReadAsStringAsync(ct);

                if (!response.IsSuccessStatusCode)
                {
                    var error = JsonSerializer.Deserialize<OneCError>(responseContent);

                    _logger.LogError("1C request failed {StatusCode}: {Error}", response.StatusCode, error);

                    return ServiceError.Failure($"1C request failed {response.StatusCode}: {error?.OdataError?.Message?.Value}");
                }

                return ServiceResult.Success();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "1C returned an invalid JSON response {Method} {Uri} {responseContent}", request.Method, request.RequestUri, responseContent);

                return ServiceError.Failure("1C returned an invalid JSON response");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "1C request failed: {Method} {Uri}", request.Method, request.RequestUri);

                return ServiceError.Failure(ex.Message);
            }
        }
    }
}
