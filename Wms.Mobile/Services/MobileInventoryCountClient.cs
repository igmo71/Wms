using System.Net.Http.Json;
using Wms.Contracts.Mobile.V1;

namespace Wms.Mobile.Services;

public sealed class MobileInventoryCountClient
{
    private readonly MobileApiTransport _transport;

    internal MobileInventoryCountClient(MobileApiTransport transport)
    {
        _transport = transport;
    }

    public async Task<IReadOnlyList<MobileInventoryCountSummaryResponse>> GetDraftsAsync(
        Guid warehouseId,
        CancellationToken ct = default)
    {
        var route = $"{MobileApiRoutes.InventoryCounts}?warehouseId={warehouseId:D}";
        using HttpResponseMessage response = await _transport.GetAsync(route, ct);
        return await response.Content.ReadFromJsonAsync<List<MobileInventoryCountSummaryResponse>>(ct)
            ?? throw MobileApiTransport.InvalidResponse(
                response,
                "Сервер вернул некорректный список инвентаризаций.");
    }

    public Task<MobileInventoryCountDetailsResponse> GetAsync(
        Guid inventoryCountId,
        CancellationToken ct = default) =>
        GetDetailsAsync($"{MobileApiRoutes.InventoryCounts}/{inventoryCountId:D}", ct);

    public Task<MobileInventoryCountDetailsResponse> StartAsync(
        Guid warehouseId,
        string storageLocationBarcode,
        Guid clientRequestId,
        CancellationToken ct = default) =>
        PostDetailsAsync(
            $"{MobileApiRoutes.InventoryCounts}/start",
            new MobileStartInventoryCountRequest(
                clientRequestId,
                warehouseId,
                storageLocationBarcode),
            ct);

    public async Task<MobileInventoryCountScanResponse> IncrementSkuAsync(
        Guid inventoryCountId,
        MobileIncrementInventoryCountSkuRequest request,
        CancellationToken ct = default)
    {
        var route = $"{MobileApiRoutes.InventoryCounts}/{inventoryCountId:D}/sku/scan";
        using HttpResponseMessage response = await _transport.PostAsync(
            route,
            request,
            ct);
        return await MobileApiTransport.ReadCommandResponseAsync<MobileInventoryCountScanResponse>(
            response,
            "Сервер вернул некорректный результат сканирования.",
            ct);
    }

    public async Task<IReadOnlyList<MobileInventoryCountSkuSearchResponse>> SearchSkusAsync(
        Guid inventoryCountId,
        string query,
        CancellationToken ct = default)
    {
        var route = $"{MobileApiRoutes.InventoryCounts}/{inventoryCountId:D}/skus"
            + $"?query={Uri.EscapeDataString(query)}";
        using HttpResponseMessage response = await _transport.GetAsync(route, ct);
        return await response.Content
            .ReadFromJsonAsync<List<MobileInventoryCountSkuSearchResponse>>(ct)
            ?? throw MobileApiTransport.InvalidResponse(
                response,
                "Сервер вернул некорректные результаты поиска товара.");
    }

    public Task<MobileInventoryCountDetailsResponse> SetSkuQuantityAsync(
        Guid inventoryCountId,
        Guid stockKeepingUnitId,
        decimal countedQuantity,
        DateOnly? expirationDate,
        bool expirationConfirmed,
        Guid clientRequestId,
        CancellationToken ct = default) =>
        PostDetailsAsync(
            $"{MobileApiRoutes.InventoryCounts}/{inventoryCountId:D}/sku-quantity",
            new MobileSetInventoryCountSkuQuantityRequest(
                clientRequestId,
                stockKeepingUnitId,
                countedQuantity,
                expirationDate,
                expirationConfirmed),
            ct);

    public Task<MobileInventoryCountDetailsResponse> SetItemQuantityAsync(
        Guid inventoryCountId,
        Guid itemId,
        decimal countedQuantity,
        bool expirationConfirmed,
        Guid clientRequestId,
        CancellationToken ct = default) =>
        PostDetailsAsync(
            $"{MobileApiRoutes.InventoryCounts}/{inventoryCountId:D}/items/{itemId:D}/quantity",
            new MobileSetInventoryCountItemQuantityRequest(clientRequestId, countedQuantity, expirationConfirmed),
            ct);

    public Task<MobileInventoryCountDetailsResponse> RemoveItemAsync(
        Guid inventoryCountId,
        Guid itemId,
        Guid clientRequestId,
        CancellationToken ct = default) =>
        PostDetailsAsync(
            $"{MobileApiRoutes.InventoryCounts}/{inventoryCountId:D}/items/{itemId:D}/remove",
            new MobileInventoryCountCommandRequest(clientRequestId),
            ct);

    public Task<MobileInventoryCountDetailsResponse> PostAsync(
        Guid inventoryCountId,
        Guid clientRequestId,
        CancellationToken ct = default) =>
        PostDetailsAsync(
            $"{MobileApiRoutes.InventoryCounts}/{inventoryCountId:D}/post",
            new MobileInventoryCountCommandRequest(clientRequestId),
            ct);

    public async Task DeleteDraftAsync(
        Guid inventoryCountId,
        Guid clientRequestId,
        CancellationToken ct = default)
    {
        var route = $"{MobileApiRoutes.InventoryCounts}/{inventoryCountId:D}/delete";
        using HttpResponseMessage response = await _transport.PostAsync(
            route,
            new MobileInventoryCountCommandRequest(clientRequestId),
            ct);
        await MobileApiTransport.ReadCommandResponseAsync<MobileInventoryCountDeletedResponse>(
            response,
            "Сервер вернул некорректный результат удаления инвентаризации.",
            ct);
    }

    private async Task<MobileInventoryCountDetailsResponse> GetDetailsAsync(
        string route,
        CancellationToken ct)
    {
        using HttpResponseMessage response = await _transport.GetAsync(route, ct);
        return await response.Content.ReadFromJsonAsync<MobileInventoryCountDetailsResponse>(ct)
            ?? throw MobileApiTransport.InvalidResponse(
                response,
                "Сервер вернул некорректную инвентаризацию.");
    }

    private async Task<MobileInventoryCountDetailsResponse> PostDetailsAsync<TRequest>(
        string route,
        TRequest request,
        CancellationToken ct)
    {
        using HttpResponseMessage response = await _transport.PostAsync(route, request, ct);
        return await MobileApiTransport.ReadCommandResponseAsync<MobileInventoryCountDetailsResponse>(
            response,
            "Сервер вернул некорректную инвентаризацию.",
            ct);
    }
}
