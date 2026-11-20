using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Wms.Contracts.Mobile.V1;

namespace Wms.Mobile.Services;

internal sealed class MobileAuthenticationHandler(CachedMobileSessionStore sessionStore)
    : DelegatingHandler(CreateInnerHandler())
{
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(1);
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (IsAnonymousRoute(request.RequestUri))
        {
            return await base.SendAsync(request, cancellationToken);
        }

        var measure = MobilePerformanceDiagnostics.CaptureHttpOperation() is not null;
        var sessionStarted = measure ? Stopwatch.GetTimestamp() : 0;
        var session = await GetCurrentSessionAsync(request, measure, cancellationToken);
        if (session is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue(
                session.TokenType, session.AccessToken);
        }
        if (measure)
        {
            request.Options.Set(MobilePerformanceDiagnostics.SessionDurationKey,
                Stopwatch.GetElapsedTime(sessionStarted));
            request.Headers.Add(MobileDiagnosticHeaders.RequestServerDuration, "1");
        }

        var response = await base.SendAsync(request, cancellationToken);
        if (measure)
        {
            response.RequestMessage ??= request;
            if (request.Options.TryGetValue(MobilePerformanceDiagnostics.SessionDurationKey, out var duration))
                response.RequestMessage.Options.Set(MobilePerformanceDiagnostics.SessionDurationKey, duration);
            if (request.Options.TryGetValue(MobilePerformanceDiagnostics.SessionRefreshKey, out var refreshed))
                response.RequestMessage.Options.Set(MobilePerformanceDiagnostics.SessionRefreshKey, refreshed);
        }
        if (session is not null && response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            sessionStore.ClearIfCurrent(session);

        return response;
    }

    private async Task<MobileSession?> GetCurrentSessionAsync(
        HttpRequestMessage apiRequest, bool measure, CancellationToken ct)
    {
        var apiRequestUri = apiRequest.RequestUri;
        var session = await sessionStore.GetAsync();
        if (session is null || session.ExpiresAtUtc > DateTimeOffset.UtcNow + RefreshMargin)
        {
            return session;
        }

        await _refreshLock.WaitAsync(ct);
        try
        {
            session = await sessionStore.GetAsync();
            if (session is null || session.ExpiresAtUtc > DateTimeOffset.UtcNow + RefreshMargin)
            {
                return session;
            }

            if (apiRequestUri is not { IsAbsoluteUri: true })
            {
                sessionStore.ClearIfCurrent(session);
                return null;
            }

            if (measure) apiRequest.Options.Set(MobilePerformanceDiagnostics.SessionRefreshKey, true);
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                new Uri(apiRequestUri, MobileApiRoutes.Refresh))
            {
                Content = JsonContent.Create(new MobileRefreshRequest(session.RefreshToken))
            };
            using var response = await base.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode is System.Net.HttpStatusCode.BadRequest
                    or System.Net.HttpStatusCode.Unauthorized)
                {
                    sessionStore.ClearIfCurrent(session);
                    return null;
                }

                throw new HttpRequestException(
                    "Сервер WMS не подтвердил обновление сессии.",
                    null,
                    response.StatusCode);
            }

            MobileSessionResponse? refreshed;
            try
            {
                refreshed = await response.Content.ReadFromJsonAsync<MobileSessionResponse>(ct);
            }
            catch (Exception exception) when (
                exception is System.Text.Json.JsonException
                    or NotSupportedException
                    or IOException)
            {
                throw new HttpRequestException(
                    "Сервер WMS вернул некорректный результат обновления сессии.",
                    exception,
                    response.StatusCode);
            }

            if (refreshed is null)
                throw new HttpRequestException(
                    "Сервер WMS вернул некорректный результат обновления сессии.",
                    null,
                    response.StatusCode);

            var renewed = ToSession(refreshed);
            return await sessionStore.TryRefreshAsync(session, renewed) ? renewed : null;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    internal static MobileSession ToSession(MobileSessionResponse response) => new(
        response.TokenType,
        response.AccessToken,
        response.RefreshToken,
        DateTimeOffset.UtcNow.AddSeconds(response.ExpiresIn));

    private static bool IsAnonymousRoute(Uri? uri) =>
        uri?.AbsolutePath is MobileApiRoutes.Login or MobileApiRoutes.Refresh;

    internal static HttpClientHandler CreateInnerHandler()
    {
        var handler = new HttpClientHandler();

#if DEBUG
        handler.ServerCertificateCustomValidationCallback =
            (request, certificate, _, errors) =>
                errors == System.Net.Security.SslPolicyErrors.None
                || (request.RequestUri?.IsLoopback == true
                    && certificate is not null
                    && string.Equals(
                        certificate.GetNameInfo(
                            System.Security.Cryptography.X509Certificates.X509NameType.DnsName,
                            forIssuer: false),
                        "localhost",
                        StringComparison.OrdinalIgnoreCase));
#endif

        return handler;
    }
}
