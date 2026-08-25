using System.Net.Http.Json;
using Wms.Contracts.Mobile.V1;

namespace Wms.Mobile.Services;

public sealed class MobileApiClient
{
    private readonly HttpClient _httpClient;
    private readonly IMobileSessionStore _sessionStore;

    internal MobileApiClient(
        HttpClient httpClient,
        IMobileSessionStore sessionStore)
    {
        _httpClient = httpClient;
        _sessionStore = sessionStore;
    }

    public async Task<MobileCurrentUserResponse> LoginAsync(
        string email,
        string password,
        CancellationToken ct = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            MobileApiRoutes.Login,
            new MobileLoginRequest(email, password),
            ct);

        if (!response.IsSuccessStatusCode)
        {
            await ThrowApiExceptionAsync(response, ct);
        }

        var tokenResponse = await response.Content.ReadFromJsonAsync<MobileSessionResponse>(ct)
            ?? throw new MobileApiException(
                response.StatusCode,
                "invalid_session_response",
                "Сервер вернул некорректный ответ сессии.");

        await _sessionStore.SaveAsync(MobileAuthenticationHandler.ToSession(tokenResponse));
        return await GetCurrentUserAsync(ct);
    }

    public async Task<MobileCurrentUserResponse> GetCurrentUserAsync(
        CancellationToken ct = default)
    {
        using var response = await _httpClient.GetAsync(MobileApiRoutes.Me, ct);
        if (!response.IsSuccessStatusCode)
        {
            await ThrowApiExceptionAsync(response, ct);
        }

        return await response.Content.ReadFromJsonAsync<MobileCurrentUserResponse>(ct)
            ?? throw new MobileApiException(
                response.StatusCode,
                "invalid_current_user_response",
                "Сервер вернул некорректные сведения о пользователе.");
    }

    public async Task<MobileStorageLocationResponse> ResolveStorageLocationAsync(
        string barcode,
        CancellationToken ct = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            MobileApiRoutes.ResolveStorageLocation,
            new MobileResolveStorageLocationRequest(barcode),
            ct);

        if (!response.IsSuccessStatusCode)
        {
            await ThrowApiExceptionAsync(response, ct);
        }

        return await response.Content.ReadFromJsonAsync<MobileStorageLocationResponse>(ct)
            ?? throw new MobileApiException(
                response.StatusCode,
                "invalid_storage_location_response",
                "Сервер вернул некорректные сведения о ячейке.");
    }

    public async Task<MobileSkuResponse> ResolveSkuAsync(
        string barcode,
        CancellationToken ct = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            MobileApiRoutes.ResolveSku,
            new MobileResolveSkuRequest(barcode),
            ct);

        if (!response.IsSuccessStatusCode)
        {
            await ThrowApiExceptionAsync(response, ct);
        }

        return await response.Content.ReadFromJsonAsync<MobileSkuResponse>(ct)
            ?? throw new MobileApiException(
                response.StatusCode,
                "invalid_sku_response",
                "Сервер вернул некорректные сведения о товаре.");
    }

    public void Logout() => _sessionStore.Clear();

    private static async Task ThrowApiExceptionAsync(
        HttpResponseMessage response,
        CancellationToken ct)
    {
        MobileProblemResponse? problem = null;
        try
        {
            problem = await response.Content.ReadFromJsonAsync<MobileProblemResponse>(ct);
        }
        catch (Exception exception) when (
            exception is System.Text.Json.JsonException or NotSupportedException)
        {
            // The fallback below deliberately avoids exposing a raw server response.
        }

        throw new MobileApiException(
            response.StatusCode,
            problem?.Code ?? "request_failed",
            problem?.Message ?? "Не удалось выполнить запрос к WMS.");
    }
}
