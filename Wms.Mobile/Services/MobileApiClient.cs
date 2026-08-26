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
        Guid? expectedWarehouseId = null,
        MobileStorageLocationContext context = MobileStorageLocationContext.AnyOperational,
        CancellationToken ct = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            MobileApiRoutes.ResolveStorageLocation,
            new MobileResolveStorageLocationRequest(barcode, expectedWarehouseId, context),
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

    public async Task<IReadOnlyList<MobileWarehouseResponse>> GetWarehousesAsync(
        CancellationToken ct = default)
    {
        using var response = await _httpClient.GetAsync(MobileApiRoutes.Warehouses, ct);
        if (!response.IsSuccessStatusCode)
        {
            await ThrowApiExceptionAsync(response, ct);
        }

        return await response.Content.ReadFromJsonAsync<List<MobileWarehouseResponse>>(ct)
            ?? throw new MobileApiException(
                response.StatusCode,
                "invalid_warehouses_response",
                "Сервер вернул некорректный список складов.");
    }

    public async Task<IReadOnlyList<MobileInventoryTransferSummaryResponse>> GetInventoryTransfersAsync(
        Guid warehouseId,
        CancellationToken ct = default)
    {
        var route = $"{MobileApiRoutes.InventoryTransfers}?warehouseId={warehouseId:D}";
        using var response = await _httpClient.GetAsync(route, ct);
        if (!response.IsSuccessStatusCode)
        {
            await ThrowApiExceptionAsync(response, ct);
        }

        return await response.Content
            .ReadFromJsonAsync<List<MobileInventoryTransferSummaryResponse>>(ct)
            ?? throw new MobileApiException(
                response.StatusCode,
                "invalid_inventory_transfers_response",
                "Сервер вернул некорректный список перемещений.");
    }

    public async Task<MobileInventoryTransferSummaryResponse> CreateInventoryTransferAsync(
        Guid warehouseId,
        Guid clientRequestId,
        CancellationToken ct = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            MobileApiRoutes.InventoryTransfers,
            new MobileCreateInventoryTransferRequest(clientRequestId, warehouseId),
            ct);
        if (!response.IsSuccessStatusCode)
        {
            await ThrowApiExceptionAsync(response, ct);
        }

        return await response.Content
            .ReadFromJsonAsync<MobileInventoryTransferSummaryResponse>(ct)
            ?? throw new MobileApiException(
                response.StatusCode,
                "invalid_inventory_transfer_response",
                "Сервер вернул некорректное перемещение.");
    }

    public async Task<MobileDirectTransferSkuResponse> ResolveDirectTransferSkuAsync(
        Guid transferId,
        Guid sourceStorageLocationId,
        string barcode,
        CancellationToken ct = default)
    {
        var route = $"{MobileApiRoutes.InventoryTransfers}/{transferId:D}/direct/sku/resolve";
        using var response = await _httpClient.PostAsJsonAsync(
            route,
            new MobileResolveDirectTransferSkuRequest(barcode, sourceStorageLocationId),
            ct);
        if (!response.IsSuccessStatusCode)
        {
            await ThrowApiExceptionAsync(response, ct);
        }

        return await response.Content.ReadFromJsonAsync<MobileDirectTransferSkuResponse>(ct)
            ?? throw new MobileApiException(
                response.StatusCode,
                "invalid_direct_transfer_sku_response",
                "Сервер вернул некорректные сведения о товаре и остатке.");
    }

    public async Task<MobileMoveDirectInventoryTransferResponse> MoveDirectAsync(
        Guid transferId,
        Guid sourceStorageLocationId,
        Guid destinationStorageLocationId,
        Guid stockKeepingUnitId,
        double quantity,
        Guid clientRequestId,
        CancellationToken ct = default)
    {
        var route = $"{MobileApiRoutes.InventoryTransfers}/{transferId:D}/direct-movements";
        using var response = await _httpClient.PostAsJsonAsync(
            route,
            new MobileMoveDirectInventoryTransferRequest(
                clientRequestId,
                sourceStorageLocationId,
                destinationStorageLocationId,
                stockKeepingUnitId,
                quantity),
            ct);
        if (!response.IsSuccessStatusCode)
        {
            await ThrowApiExceptionAsync(response, ct);
        }

        return await response.Content
            .ReadFromJsonAsync<MobileMoveDirectInventoryTransferResponse>(ct)
            ?? throw new MobileApiException(
                response.StatusCode,
                "invalid_direct_movement_response",
                "Сервер вернул некорректный результат перемещения.");
    }

    public async Task<MobileCompleteInventoryTransferResponse> CompleteInventoryTransferAsync(
        Guid transferId,
        Guid clientRequestId,
        CancellationToken ct = default)
    {
        var route = $"{MobileApiRoutes.InventoryTransfers}/{transferId:D}/complete";
        using var response = await _httpClient.PostAsJsonAsync(
            route,
            new MobileCompleteInventoryTransferRequest(clientRequestId),
            ct);
        if (!response.IsSuccessStatusCode)
        {
            await ThrowApiExceptionAsync(response, ct);
        }

        return await response.Content
            .ReadFromJsonAsync<MobileCompleteInventoryTransferResponse>(ct)
            ?? throw new MobileApiException(
                response.StatusCode,
                "invalid_inventory_transfer_completion_response",
                "Сервер вернул некорректный результат завершения перемещения.");
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
