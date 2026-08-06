using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using Wms.Common;

namespace Wms.Integration.OneS;

public class OneCClient(HttpClient httpClient, ILogger<OneCClient> logger)
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly ILogger<OneCClient> _logger = logger;

    public async Task<ServiceResult<TResponse?>> GetValueAsync<TResponse>(string uri, CancellationToken ct = default)
    {
        using var scope = _logger.BeginScope("OneCClient GetValue {Uri}", uri);

        using var response = await _httpClient.GetAsync(uri, ct);

        var responseContent = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            try
            {
                var error = JsonSerializer.Deserialize<OneCError>(responseContent);

                _logger.LogError("{StatusCode} {@Error}", response.StatusCode, error);

                return ServiceError.Failure<TResponse>(error?.OdataError?.Message?.Value);
            }
            catch (Exception)
            {
                _logger.LogError("{StatusCode} {ErrorContent}", response.StatusCode, responseContent);

                return ServiceError.Failure<TResponse>(responseContent.Length > 100 ? responseContent[..100] : responseContent);
            }
        }

        var result = JsonSerializer.Deserialize<TResponse>(responseContent);

        return result;
    }

    public async Task<ServiceResult<TResponse?>> PatchValueAsync<TRequest, TResponse>(string uri, TRequest request, CancellationToken ct = default)
    {
        using var scope = _logger.BeginScope("OneCClient PatchValue {Uri}", uri);

        var json = JsonSerializer.Serialize(request);

        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            using var response = await _httpClient.PatchAsync(uri, content, ct);

            var responseContent = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                try
                {
                    var error = JsonSerializer.Deserialize<OneCError>(responseContent);

                    _logger.LogError("{StatusCode} {@Error}", response.StatusCode, error);

                    return ServiceError.Failure<TResponse>(error?.OdataError?.Message?.Value);
                }
                catch (Exception)
                {
                    _logger.LogError("{StatusCode} {ErrorContent}", response.StatusCode, responseContent);

                    return ServiceError.Failure<TResponse>(responseContent.Length > 100 ? responseContent[..100] : responseContent);
                }
            }

            var result = JsonSerializer.Deserialize<TResponse>(responseContent);

            return result;

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{@Exception}", ex.Message);

            return ServiceError.Failure<TResponse>(ex.Message);
        }
    }

    public async Task<ServiceResult<TResponse?>> PostValueAsync<TRequest, TResponse>(string uri, TRequest request, CancellationToken ct = default)
    {
        using var scope = _logger.BeginScope("OneCClient PostValue {Uri}", uri);

        var json = JsonSerializer.Serialize(request);

        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            using var response = await _httpClient.PostAsync(uri, content, ct);

            var responseContent = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                try
                {
                    var error = JsonSerializer.Deserialize<OneCError>(responseContent);

                    _logger.LogError("{StatusCode} {@Error}", response.StatusCode, error);

                    return ServiceError.Failure<TResponse>(error?.OdataError?.Message?.Value);
                }
                catch (Exception)
                {
                    _logger.LogError("{StatusCode} {ErrorContent}", response.StatusCode, responseContent);

                    return ServiceError.Failure<TResponse>(responseContent.Length > 100 ? responseContent[..100] : responseContent);
                }
            }

            var result = JsonSerializer.Deserialize<TResponse>(responseContent);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{@Exception}", ex.Message);

            return ServiceError.Failure<TResponse>(ex.Message);
        }
    }

    public async Task<ServiceResult> PostValueAsync(string uri, CancellationToken ct = default)
    {
        using var scope = _logger.BeginScope("OneCClient PostValue {Uri}", uri);

        try
        {
            using var response = await _httpClient.PostAsync(uri, null, ct);

            var responseContent = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                try
                {
                    var error = JsonSerializer.Deserialize<OneCError>(responseContent);

                    _logger.LogError("{StatusCode} {@Error}", response.StatusCode, error);

                    return ServiceError.Failure(error?.OdataError?.Message?.Value);
                }
                catch (Exception)
                {
                    _logger.LogError("{StatusCode} {ErrorContent}", response.StatusCode, responseContent);

                    return ServiceError.Failure(responseContent.Length > 100 ? responseContent[..100] : responseContent);
                }
            }

            return ServiceResult.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{@Exception}", ex.Message);

            return ServiceError.Failure(ex.Message);
        }
    }
}
