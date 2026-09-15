using System.Net.Http.Json;
using Wms.Contracts.Mobile.V1;

namespace Wms.Mobile.Services;

public sealed class MobileReceivingOrderClient
{
    private readonly MobileApiTransport _transport;

    internal MobileReceivingOrderClient(MobileApiTransport transport)
    {
        _transport = transport;
    }

    public async Task<MobileReceivingOrderWorkQueueResponse> GetWorkQueueAsync(
        Guid warehouseId,
        CancellationToken ct = default)
    {
        var route = $"{MobileApiRoutes.ReceivingOrders}?warehouseId={warehouseId:D}";
        using HttpResponseMessage response = await _transport.GetAsync(route, ct);
        return await response.Content.ReadFromJsonAsync<MobileReceivingOrderWorkQueueResponse>(ct)
            ?? throw MobileApiTransport.InvalidResponse(
                response,
                "Сервер вернул некорректную очередь приёмки и размещения.");
    }

    public Task<MobileReceivingOrderDetailsResponse> GetAsync(
        Guid orderId,
        CancellationToken ct = default) =>
        GetDetailsAsync($"{MobileApiRoutes.ReceivingOrders}/{orderId:D}", ct);

    public async Task<MobileReceivingOrderDetailsResponse> ResolveDocumentAsync(
        Guid warehouseId,
        string barcode,
        CancellationToken ct = default)
    {
        var route = $"{MobileApiRoutes.ReceivingOrders}/resolve-document";
        using HttpResponseMessage response = await _transport.PostAsync(
            route,
            new MobileResolveReceivingOrderDocumentRequest(warehouseId, barcode),
            ct);
        return await response.Content.ReadFromJsonAsync<MobileReceivingOrderDetailsResponse>(ct)
            ?? throw MobileApiTransport.InvalidResponse(
                response,
                "Сервер вернул некорректный приходный ордер.");
    }

    public Task<MobileReceivingOrderCommandResponse> JoinAsync(
        Guid orderId,
        string? receivingLocationBarcode,
        Guid clientRequestId,
        CancellationToken ct = default) =>
        PostCommandAsync(
            $"{MobileApiRoutes.ReceivingOrders}/{orderId:D}/join-receiving",
            new MobileJoinReceivingOrderRequest(clientRequestId, receivingLocationBarcode),
            ct);

    public async Task<IReadOnlyList<MobileReceivingOrderLineCandidateResponse>> ResolveSkuAsync(
        Guid orderId,
        string barcode,
        CancellationToken ct = default)
    {
        var route = $"{MobileApiRoutes.ReceivingOrders}/{orderId:D}/lines/resolve-sku";
        using HttpResponseMessage response = await _transport.PostAsync(
            route,
            new MobileResolveReceivingOrderSkuRequest(barcode),
            ct);
        return await response.Content
            .ReadFromJsonAsync<List<MobileReceivingOrderLineCandidateResponse>>(ct)
            ?? throw MobileApiTransport.InvalidResponse(
                response,
                "Сервер вернул некорректные строки товара приходного ордера.");
    }

    public async Task<MobileReceivingOrderLineSearchResponse> SearchLinesAsync(
        Guid orderId,
        string query,
        CancellationToken ct = default)
    {
        var route = $"{MobileApiRoutes.ReceivingOrders}/{orderId:D}/lines/search"
            + $"?query={Uri.EscapeDataString(query)}";
        using HttpResponseMessage response = await _transport.GetAsync(route, ct);
        return await response.Content.ReadFromJsonAsync<MobileReceivingOrderLineSearchResponse>(ct)
            ?? throw MobileApiTransport.InvalidResponse(
                response,
                "Сервер вернул некорректные результаты поиска строк приходного ордера.");
    }

    public Task<MobileReceivingOrderCommandResponse> IncrementLineAsync(
        Guid orderId,
        int lineNumber,
        Guid clientRequestId,
        CancellationToken ct = default) =>
        PostCommandAsync(
            $"{MobileApiRoutes.ReceivingOrders}/{orderId:D}/lines/{lineNumber}/scan",
            new MobileReceivingOrderCommandRequest(clientRequestId),
            ct);

    public Task<MobileReceivingOrderCommandResponse> SetLineQuantityAsync(
        Guid orderId,
        int lineNumber,
        decimal quantity,
        Guid clientRequestId,
        CancellationToken ct = default) =>
        PostCommandAsync(
            $"{MobileApiRoutes.ReceivingOrders}/{orderId:D}/lines/{lineNumber}/quantity",
            new MobileSetReceivingOrderLineQuantityRequest(clientRequestId, quantity),
            ct);

    public Task<MobileReceivingOrderCommandResponse> CompleteAsync(
        Guid orderId,
        Guid clientRequestId,
        CancellationToken ct = default) =>
        PostCommandAsync(
            $"{MobileApiRoutes.ReceivingOrders}/{orderId:D}/complete-receiving",
            new MobileReceivingOrderCommandRequest(clientRequestId),
            ct);

    public Task<MobileReceivingOrderCommandResponse> StartPutawayAsync(
        Guid orderId,
        Guid clientRequestId,
        CancellationToken ct = default) =>
        PostCommandAsync(
            $"{MobileApiRoutes.ReceivingOrders}/{orderId:D}/start-putaway",
            new MobileReceivingOrderCommandRequest(clientRequestId),
            ct);

    public Task<MobileReceivingOrderCommandResponse> AddPutawayMovementAsync(
        Guid orderId,
        int lineNumber,
        string destinationStorageLocationBarcode,
        decimal quantity,
        Guid clientRequestId,
        CancellationToken ct = default) =>
        PostCommandAsync(
            $"{MobileApiRoutes.ReceivingOrders}/{orderId:D}/putaway-movements",
            new MobileAddReceivingOrderPutawayMovementRequest(
                clientRequestId,
                lineNumber,
                destinationStorageLocationBarcode,
                quantity),
            ct);

    public Task<MobileReceivingOrderCommandResponse> DeletePutawayMovementAsync(
        Guid orderId,
        Guid movementId,
        Guid clientRequestId,
        CancellationToken ct = default) =>
        PostCommandAsync(
            $"{MobileApiRoutes.ReceivingOrders}/{orderId:D}/putaway-movements/{movementId:D}/delete",
            new MobileReceivingOrderCommandRequest(clientRequestId),
            ct);

    public Task<MobileReceivingOrderCommandResponse> CompletePutawayAsync(
        Guid orderId,
        Guid clientRequestId,
        CancellationToken ct = default) =>
        PostCommandAsync(
            $"{MobileApiRoutes.ReceivingOrders}/{orderId:D}/complete-putaway",
            new MobileReceivingOrderCommandRequest(clientRequestId),
            ct);

    private async Task<MobileReceivingOrderDetailsResponse> GetDetailsAsync(
        string route,
        CancellationToken ct)
    {
        using HttpResponseMessage response = await _transport.GetAsync(route, ct);
        return await response.Content.ReadFromJsonAsync<MobileReceivingOrderDetailsResponse>(ct)
            ?? throw MobileApiTransport.InvalidResponse(
                response,
                "Сервер вернул некорректный приходный ордер.");
    }

    private async Task<MobileReceivingOrderCommandResponse> PostCommandAsync<TRequest>(
        string route,
        TRequest request,
        CancellationToken ct)
    {
        using HttpResponseMessage response = await _transport.PostAsync(route, request, ct);
        return await MobileApiTransport.ReadCommandResponseAsync<MobileReceivingOrderCommandResponse>(
            response,
            "Сервер вернул некорректный результат операции с приходным ордером.",
            ct);
    }
}
