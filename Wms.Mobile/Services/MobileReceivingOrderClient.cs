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

    public Task<MobileReceivingOrderCommandResponse> OpenLicensePlateNumberAsync(
        Guid orderId,
        string code,
        Guid clientRequestId,
        CancellationToken ct = default) =>
        PostCommandAsync(
            $"{MobileApiRoutes.ReceivingOrders}/{orderId:D}/lpns/open",
            new MobileOpenReceivingLicensePlateNumberRequest(clientRequestId, code),
            ct);

    public Task<MobileReceivingOrderCommandResponse> CancelLicensePlateNumberAsync(
        Guid orderId,
        Guid lpnId,
        Guid clientRequestId,
        CancellationToken ct = default) =>
        PostCommandAsync(
            $"{MobileApiRoutes.ReceivingOrders}/{orderId:D}/lpns/{lpnId:D}/cancel",
            new MobileReceivingOrderCommandRequest(clientRequestId),
            ct);

    public Task<MobileReceivingOrderCommandResponse> SetLicensePlateNumberItemQuantityAsync(
        Guid orderId,
        Guid lpnId,
        Guid skuId,
        decimal quantity,
        Guid clientRequestId,
        CancellationToken ct = default) =>
        PostCommandAsync(
            $"{MobileApiRoutes.ReceivingOrders}/{orderId:D}/lpns/{lpnId:D}/items/{skuId:D}/quantity",
            new MobileSetReceivingLicensePlateNumberItemQuantityRequest(clientRequestId, quantity),
            ct);

    public Task<MobileReceivingOrderCommandResponse> RemoveLicensePlateNumberItemAsync(
        Guid orderId,
        Guid lpnId,
        Guid skuId,
        Guid clientRequestId,
        CancellationToken ct = default) =>
        PostCommandAsync(
            $"{MobileApiRoutes.ReceivingOrders}/{orderId:D}/lpns/{lpnId:D}/items/{skuId:D}/remove",
            new MobileReceivingOrderCommandRequest(clientRequestId),
            ct);

    public Task<MobileReceivingOrderCommandResponse> CloseLicensePlateNumberAsync(
        Guid orderId,
        Guid lpnId,
        Guid clientRequestId,
        CancellationToken ct = default) =>
        PostCommandAsync(
            $"{MobileApiRoutes.ReceivingOrders}/{orderId:D}/lpns/{lpnId:D}/close",
            new MobileReceivingOrderCommandRequest(clientRequestId),
            ct);

    public Task<MobileReceivingOrderCommandResponse> ReopenLicensePlateNumberAsync(
        Guid orderId,
        Guid lpnId,
        Guid clientRequestId,
        CancellationToken ct = default) =>
        PostCommandAsync(
            $"{MobileApiRoutes.ReceivingOrders}/{orderId:D}/lpns/{lpnId:D}/reopen",
            new MobileReceivingOrderCommandRequest(clientRequestId),
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
