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
        Guid? transitStorageLocationId = null,
        CancellationToken ct = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            MobileApiRoutes.InventoryTransfers,
            new MobileCreateInventoryTransferRequest(
                clientRequestId,
                warehouseId,
                transitStorageLocationId),
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

    public async Task<MobileInventoryTransferSummaryResponse?>
        GetInventoryTransferByTransitStorageLocationAsync(
            Guid transitStorageLocationId,
            CancellationToken ct = default)
    {
        var route = $"{MobileApiRoutes.InventoryTransfers}/by-transit-location/"
            + transitStorageLocationId.ToString("D");
        using var response = await _httpClient.GetAsync(route, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
        {
            return null;
        }

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

    public async Task<MobileInventoryTransferDetailsResponse> GetInventoryTransferAsync(
        Guid transferId,
        CancellationToken ct = default)
    {
        var route = $"{MobileApiRoutes.InventoryTransfers}/{transferId:D}";
        using var response = await _httpClient.GetAsync(route, ct);
        if (!response.IsSuccessStatusCode)
        {
            await ThrowApiExceptionAsync(response, ct);
        }

        return await response.Content
            .ReadFromJsonAsync<MobileInventoryTransferDetailsResponse>(ct)
            ?? throw new MobileApiException(
                response.StatusCode,
                "invalid_inventory_transfer_details_response",
                "Сервер вернул некорректную историю перемещения.");
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

    public async Task<IReadOnlyList<MobileDirectTransferSkuSearchResponse>>
        SearchDirectTransferSkusAsync(
            Guid transferId,
            Guid sourceStorageLocationId,
            string query,
            CancellationToken ct = default)
    {
        var route = $"{MobileApiRoutes.InventoryTransfers}/{transferId:D}/direct/skus"
            + $"?sourceStorageLocationId={sourceStorageLocationId:D}"
            + $"&query={Uri.EscapeDataString(query)}";
        using var response = await _httpClient.GetAsync(route, ct);
        if (!response.IsSuccessStatusCode)
        {
            await ThrowApiExceptionAsync(response, ct);
        }

        return await response.Content
            .ReadFromJsonAsync<List<MobileDirectTransferSkuSearchResponse>>(ct)
            ?? throw new MobileApiException(
                response.StatusCode,
                "invalid_direct_transfer_sku_search_response",
                "Сервер вернул некорректные результаты поиска товара.");
    }

    public async Task<MobileDirectTransferSkuResponse> ResolveTransitTransferSkuAsync(
        Guid transferId,
        string barcode,
        CancellationToken ct = default)
    {
        var route = $"{MobileApiRoutes.InventoryTransfers}/{transferId:D}/transit/sku/resolve";
        using var response = await _httpClient.PostAsJsonAsync(
            route,
            new MobileResolveTransitTransferSkuRequest(barcode),
            ct);
        if (!response.IsSuccessStatusCode)
        {
            await ThrowApiExceptionAsync(response, ct);
        }

        return await response.Content.ReadFromJsonAsync<MobileDirectTransferSkuResponse>(ct)
            ?? throw new MobileApiException(
                response.StatusCode,
                "invalid_transit_transfer_sku_response",
                "Сервер вернул некорректные сведения о товаре в транзитной ячейке.");
    }

    public async Task<IReadOnlyList<MobileDirectTransferSkuSearchResponse>>
        SearchTransitTransferSkusAsync(
            Guid transferId,
            string query,
            CancellationToken ct = default)
    {
        var route = $"{MobileApiRoutes.InventoryTransfers}/{transferId:D}/transit/skus"
            + $"?query={Uri.EscapeDataString(query)}";
        using var response = await _httpClient.GetAsync(route, ct);
        if (!response.IsSuccessStatusCode)
        {
            await ThrowApiExceptionAsync(response, ct);
        }

        return await response.Content
            .ReadFromJsonAsync<List<MobileDirectTransferSkuSearchResponse>>(ct)
            ?? throw new MobileApiException(
                response.StatusCode,
                "invalid_transit_transfer_sku_search_response",
                "Сервер вернул некорректные результаты поиска товара.");
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

    public async Task<MobileTransitInventoryTransferMovementResponse> PickToTransitAsync(
        Guid transferId,
        Guid sourceStorageLocationId,
        Guid stockKeepingUnitId,
        double quantity,
        Guid clientRequestId,
        CancellationToken ct = default)
    {
        var route = $"{MobileApiRoutes.InventoryTransfers}/{transferId:D}/pick-to-transit";
        using var response = await _httpClient.PostAsJsonAsync(
            route,
            new MobilePickToTransitRequest(
                clientRequestId,
                sourceStorageLocationId,
                stockKeepingUnitId,
                quantity),
            ct);
        if (!response.IsSuccessStatusCode)
        {
            await ThrowApiExceptionAsync(response, ct);
        }

        return await response.Content
            .ReadFromJsonAsync<MobileTransitInventoryTransferMovementResponse>(ct)
            ?? throw new MobileApiException(
                response.StatusCode,
                "invalid_pick_to_transit_response",
                "Сервер вернул некорректный результат перемещения в транзитную ячейку.");
    }

    public async Task<MobileTransitInventoryTransferMovementResponse> PutFromTransitAsync(
        Guid transferId,
        Guid destinationStorageLocationId,
        Guid stockKeepingUnitId,
        double quantity,
        Guid clientRequestId,
        CancellationToken ct = default)
    {
        var route = $"{MobileApiRoutes.InventoryTransfers}/{transferId:D}/put-from-transit";
        using var response = await _httpClient.PostAsJsonAsync(
            route,
            new MobilePutFromTransitRequest(
                clientRequestId,
                destinationStorageLocationId,
                stockKeepingUnitId,
                quantity),
            ct);
        if (!response.IsSuccessStatusCode)
        {
            await ThrowApiExceptionAsync(response, ct);
        }

        return await response.Content
            .ReadFromJsonAsync<MobileTransitInventoryTransferMovementResponse>(ct)
            ?? throw new MobileApiException(
                response.StatusCode,
                "invalid_put_from_transit_response",
                "Сервер вернул некорректный результат перемещения из транзитной ячейки.");
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

    public async Task<IReadOnlyList<MobileInventoryCountSummaryResponse>> GetInventoryCountDraftsAsync(
        Guid warehouseId,
        CancellationToken ct = default)
    {
        var route = $"{MobileApiRoutes.InventoryCounts}?warehouseId={warehouseId:D}";
        using var response = await _httpClient.GetAsync(route, ct);
        if (!response.IsSuccessStatusCode)
            await ThrowApiExceptionAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<List<MobileInventoryCountSummaryResponse>>(ct)
            ?? throw InvalidResponse(response, "invalid_inventory_counts_response", "Сервер вернул некорректный список инвентаризаций.");
    }

    public async Task<MobileInventoryCountDetailsResponse> GetInventoryCountAsync(
        Guid inventoryCountId,
        CancellationToken ct = default) =>
        await GetInventoryCountDetailsAsync($"{MobileApiRoutes.InventoryCounts}/{inventoryCountId:D}", ct);

    public async Task<MobileInventoryCountDetailsResponse> StartInventoryCountAsync(
        Guid warehouseId,
        string storageLocationBarcode,
        Guid clientRequestId,
        CancellationToken ct = default) =>
        await PostInventoryCountDetailsAsync(
            $"{MobileApiRoutes.InventoryCounts}/start",
            new MobileStartInventoryCountRequest(clientRequestId, warehouseId, storageLocationBarcode),
            ct);

    public async Task<MobileInventoryCountScanResponse> IncrementInventoryCountSkuAsync(
        Guid inventoryCountId,
        string barcode,
        Guid clientRequestId,
        CancellationToken ct = default)
    {
        var route = $"{MobileApiRoutes.InventoryCounts}/{inventoryCountId:D}/sku/scan";
        using var response = await _httpClient.PostAsJsonAsync(
            route,
            new MobileIncrementInventoryCountSkuRequest(clientRequestId, barcode),
            ct);
        if (!response.IsSuccessStatusCode)
            await ThrowApiExceptionAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<MobileInventoryCountScanResponse>(ct)
            ?? throw InvalidResponse(response, "invalid_inventory_count_scan_response", "Сервер вернул некорректный результат сканирования.");
    }

    public async Task<IReadOnlyList<MobileInventoryCountSkuSearchResponse>> SearchInventoryCountSkusAsync(
        Guid inventoryCountId,
        string query,
        CancellationToken ct = default)
    {
        var route = $"{MobileApiRoutes.InventoryCounts}/{inventoryCountId:D}/skus?query={Uri.EscapeDataString(query)}";
        using var response = await _httpClient.GetAsync(route, ct);
        if (!response.IsSuccessStatusCode)
            await ThrowApiExceptionAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<List<MobileInventoryCountSkuSearchResponse>>(ct)
            ?? throw InvalidResponse(response, "invalid_inventory_count_sku_search_response", "Сервер вернул некорректные результаты поиска товара.");
    }

    public Task<MobileInventoryCountDetailsResponse> SetInventoryCountSkuQuantityAsync(
        Guid inventoryCountId,
        Guid stockKeepingUnitId,
        double countedQuantity,
        Guid clientRequestId,
        CancellationToken ct = default) =>
        PostInventoryCountDetailsAsync(
            $"{MobileApiRoutes.InventoryCounts}/{inventoryCountId:D}/sku-quantity",
            new MobileSetInventoryCountSkuQuantityRequest(
                clientRequestId,
                stockKeepingUnitId,
                countedQuantity),
            ct);

    public Task<MobileInventoryCountDetailsResponse> SetInventoryCountItemQuantityAsync(
        Guid inventoryCountId,
        Guid itemId,
        double countedQuantity,
        Guid clientRequestId,
        CancellationToken ct = default) =>
        PostInventoryCountDetailsAsync(
            $"{MobileApiRoutes.InventoryCounts}/{inventoryCountId:D}/items/{itemId:D}/quantity",
            new MobileSetInventoryCountItemQuantityRequest(clientRequestId, countedQuantity),
            ct);

    public Task<MobileInventoryCountDetailsResponse> RemoveInventoryCountItemAsync(
        Guid inventoryCountId,
        Guid itemId,
        Guid clientRequestId,
        CancellationToken ct = default) =>
        PostInventoryCountDetailsAsync(
            $"{MobileApiRoutes.InventoryCounts}/{inventoryCountId:D}/items/{itemId:D}/remove",
            new MobileInventoryCountCommandRequest(clientRequestId),
            ct);

    public Task<MobileInventoryCountDetailsResponse> PostInventoryCountAsync(
        Guid inventoryCountId,
        Guid clientRequestId,
        CancellationToken ct = default) =>
        PostInventoryCountDetailsAsync(
            $"{MobileApiRoutes.InventoryCounts}/{inventoryCountId:D}/post",
            new MobileInventoryCountCommandRequest(clientRequestId),
            ct);

    public async Task DeleteInventoryCountDraftAsync(
        Guid inventoryCountId,
        Guid clientRequestId,
        CancellationToken ct = default)
    {
        var route = $"{MobileApiRoutes.InventoryCounts}/{inventoryCountId:D}/delete";
        using var response = await _httpClient.PostAsJsonAsync(
            route,
            new MobileInventoryCountCommandRequest(clientRequestId),
            ct);
        if (!response.IsSuccessStatusCode)
            await ThrowApiExceptionAsync(response, ct);
    }

    private async Task<MobileInventoryCountDetailsResponse> GetInventoryCountDetailsAsync(
        string route,
        CancellationToken ct)
    {
        using var response = await _httpClient.GetAsync(route, ct);
        if (!response.IsSuccessStatusCode)
            await ThrowApiExceptionAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<MobileInventoryCountDetailsResponse>(ct)
            ?? throw InvalidResponse(response, "invalid_inventory_count_response", "Сервер вернул некорректную инвентаризацию.");
    }

    private async Task<MobileInventoryCountDetailsResponse> PostInventoryCountDetailsAsync<TRequest>(
        string route,
        TRequest request,
        CancellationToken ct)
    {
        using var response = await _httpClient.PostAsJsonAsync(route, request, ct);
        if (!response.IsSuccessStatusCode)
            await ThrowApiExceptionAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<MobileInventoryCountDetailsResponse>(ct)
            ?? throw InvalidResponse(response, "invalid_inventory_count_response", "Сервер вернул некорректную инвентаризацию.");
    }

    private static MobileApiException InvalidResponse(
        HttpResponseMessage response,
        string code,
        string message) => new(response.StatusCode, code, message);

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
