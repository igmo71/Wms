using System.Net.Http.Headers;
using System.Net.Http.Json;
using Wms.Contracts.Mobile.V1;

namespace Wms.Mobile.Services;

internal sealed class MobileAuthenticationHandler(IMobileSessionStore sessionStore)
    : DelegatingHandler(new HttpClientHandler())
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

        var session = await GetCurrentSessionAsync(request.RequestUri, cancellationToken);
        if (session is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue(
                session.TokenType,
                session.AccessToken);
        }

        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            sessionStore.Clear();
        }

        return response;
    }

    private async Task<MobileSession?> GetCurrentSessionAsync(Uri? apiRequestUri, CancellationToken ct)
    {
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
                sessionStore.Clear();
                return null;
            }

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                new Uri(apiRequestUri, MobileApiRoutes.Refresh))
            {
                Content = JsonContent.Create(new MobileRefreshRequest(session.RefreshToken))
            };
            using var response = await base.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                sessionStore.Clear();
                return null;
            }

            var refreshed = await response.Content.ReadFromJsonAsync<MobileSessionResponse>(ct);
            if (refreshed is null)
            {
                sessionStore.Clear();
                return null;
            }

            session = ToSession(refreshed);
            await sessionStore.SaveAsync(session);
            return session;
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
}
