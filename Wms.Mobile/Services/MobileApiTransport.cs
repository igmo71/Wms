using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Wms.Contracts.Mobile.V1;

namespace Wms.Mobile.Services;

internal sealed class MobileApiTransport(HttpClient httpClient)
{
    public Task<HttpResponseMessage> GetAsync(
        string route,
        CancellationToken ct = default)
        => SendAsync(HttpMethod.Get, route, () => httpClient.GetAsync(route, ct), ct);

    public Task<HttpResponseMessage> PostAsync<TRequest>(
        string route,
        TRequest request,
        CancellationToken ct = default)
        => SendAsync(HttpMethod.Post, route, () => httpClient.PostAsJsonAsync(route, request, ct), ct);

    public Task<HttpResponseMessage> PutAsync<TRequest>(
        string route,
        TRequest request,
        CancellationToken ct = default)
        => SendAsync(HttpMethod.Put, route, () => httpClient.PutAsJsonAsync(route, request, ct), ct);

    public Task<HttpResponseMessage> DeleteAsync<TRequest>(
        string route,
        TRequest request,
        CancellationToken ct = default)
        => SendAsync(HttpMethod.Delete, route, async () =>
        {
            using var message = new HttpRequestMessage(HttpMethod.Delete, route)
            {
                Content = JsonContent.Create(request)
            };
            return await httpClient.SendAsync(message, ct);
        }, ct);

    private static async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string route,
        Func<Task<HttpResponseMessage>> send,
        CancellationToken ct)
    {
        var operation = MobilePerformanceDiagnostics.CaptureHttpOperation();
        var started = operation is null ? 0 : Stopwatch.GetTimestamp();
        var failed = true;
        int? statusCode = null;
        TimeSpan? sessionDuration = null;
        TimeSpan? serverDuration = null;
        var sessionRefreshed = false;
        try
        {
            HttpResponseMessage response = await send();
            statusCode = (int)response.StatusCode;
            if (operation is not null)
            {
                if (response.RequestMessage is { } request)
                {
                    if (request.Options.TryGetValue(MobilePerformanceDiagnostics.SessionDurationKey, out var duration))
                        sessionDuration = duration;
                    request.Options.TryGetValue(MobilePerformanceDiagnostics.SessionRefreshKey, out sessionRefreshed);
                }
                serverDuration = ReadServerDuration(response);
            }
            if (!response.IsSuccessStatusCode)
            {
                try
                {
                    throw await CreateApiExceptionAsync(response, ct);
                }
                finally
                {
                    response.Dispose();
                }
            }

            failed = false;
            return response;
        }
        catch (OperationCanceledException exception) when (!ct.IsCancellationRequested)
        {
            // HttpClient timeout uses cancellation, but the server may have committed.
            // Route it through the existing uncertain-result handling, without retrying.
            throw new HttpRequestException(
                "Время ожидания ответа WMS истекло. Результат запроса не подтверждён.",
                exception);
        }
        finally
        {
            operation?.RecordHttp(method.Method, route, started,
                Stopwatch.GetElapsedTime(started), statusCode, failed,
                sessionDuration, serverDuration, sessionRefreshed);
        }
    }

    private static TimeSpan? ReadServerDuration(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues(MobileDiagnosticHeaders.ServerDurationMilliseconds, out var values)
            || !decimal.TryParse(values.FirstOrDefault(), NumberStyles.Float,
                CultureInfo.InvariantCulture, out var milliseconds)
            || milliseconds < 0
            || milliseconds > TimeSpan.MaxValue.Ticks / (decimal)TimeSpan.TicksPerMillisecond)
            return null;
        return TimeSpan.FromTicks((long)(milliseconds * TimeSpan.TicksPerMillisecond));
    }

    public static async Task<TResponse> ReadCommandResponseAsync<TResponse>(
        HttpResponseMessage response,
        string message,
        CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<TResponse>(ct)
                ?? throw InvalidResponse(response, message);
        }
        catch (Exception exception) when (
            exception is JsonException or NotSupportedException or IOException)
        {
            throw new HttpRequestException(message, exception, response.StatusCode);
        }
        catch (OperationCanceledException exception) when (!ct.IsCancellationRequested)
        {
            throw new HttpRequestException(message, exception, response.StatusCode);
        }
    }

    public static HttpRequestException InvalidResponse(
        HttpResponseMessage response,
        string message) => new(message, null, response.StatusCode);

    private static async Task<Exception> CreateApiExceptionAsync(
        HttpResponseMessage response,
        CancellationToken ct)
    {
        MobileProblemResponse? problem = null;
        try
        {
            problem = await response.Content.ReadFromJsonAsync<MobileProblemResponse>(ct);
        }
        catch (Exception exception) when (
            exception is JsonException or NotSupportedException)
        {
            // The fallback below deliberately avoids exposing a raw server response.
        }

        if (response.StatusCode == HttpStatusCode.RequestTimeout
            || (int)response.StatusCode >= 500)
        {
            return new HttpRequestException(
                problem?.Message ?? "Сервер WMS не подтвердил результат операции.",
                null,
                response.StatusCode);
        }

        return new MobileApiException(
            response.StatusCode,
            problem?.Code ?? "request_failed",
            problem?.Message ?? "Не удалось выполнить запрос к WMS.");
    }
}
