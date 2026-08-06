using Microsoft.Extensions.Logging;
using System.Net;
using System.Text;
using System.Text.Json;
using Wms.Common;

namespace Wms.Integration.OneS;

public class OneCClient(HttpClient httpClient, ILogger<OneCClient> logger)
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly ILogger<OneCClient> _logger = logger;

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
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{@Exception}", ex.Message);

            return ServiceError.Failure(ex.Message);
        }
    }

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
        var request = CreateJsonRequest(HttpMethod.Patch, uri, value);

        return SendAsync<TResponse>(request, ct);
    }
    public Task<ServiceResult<TResponse?>> PostValueAsync<TRequest, TResponse>(
        string uri,
        TRequest value,
        CancellationToken ct = default)
    {
        var request = CreateJsonRequest(HttpMethod.Post, uri, value);

        return SendAsync<TResponse>(request, ct);
    }

    private static HttpRequestMessage CreateJsonRequest<TRequest>(HttpMethod method, string uri, TRequest value)
    {
        var json = JsonSerializer.Serialize(value);

        return new HttpRequestMessage(method, uri)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private async Task<ServiceResult<TResponse?>> SendAsync<TResponse>(HttpRequestMessage request, CancellationToken ct)
    {
        using (request)
        {
            using var scope = _logger.BeginScope("OneCClient {Method} {Uri}", request.Method, request.RequestUri);

            try
            {
                using var response = await _httpClient.SendAsync(request, ct);

                var responseContent = await response.Content.ReadAsStringAsync(ct);

                if (!response.IsSuccessStatusCode)
                {
                    return CreateFailure<TResponse>(response.StatusCode, responseContent);
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
                _logger.LogError(ex, "Failed to deserialize 1C response for {Method} {Uri}",
                    request.Method, request.RequestUri);

                return ServiceError.Failure<TResponse>("1C returned an invalid JSON response.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "1C request failed: {Method} {Uri}",
                    request.Method, request.RequestUri);

                return ServiceError.Failure<TResponse>(ex.Message);
            }
        }

    }

    private ServiceResult<TResponse?> CreateFailure<TResponse>(HttpStatusCode statusCode, string responseContent)
    {
        var message = TryReadErrorMessage(responseContent)
            ?? Truncate(responseContent, 100);

        _logger.LogError("1C request failed with status {StatusCode}: {ErrorMessage}",
            statusCode, message);

        return statusCode switch
        {
            HttpStatusCode.NotFound => ServiceError.NotFound<TResponse>(message),
            HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity => ServiceError.Invalid<TResponse>(message),
            _ => ServiceError.Failure<TResponse>(message)
        };
    }

    private static string? TryReadErrorMessage(string responseContent)
    {
        if (string.IsNullOrWhiteSpace(responseContent))
            return null;

        try
        {
            var error = JsonSerializer.Deserialize<OneCError>(responseContent);

            return error?.OdataError?.Message?.Value;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
