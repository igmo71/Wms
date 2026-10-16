using System.Net.Http.Json;
using Wms.Contracts.Mobile.V1;

namespace Wms.Mobile.Services;

public sealed class MobileReceivingOrderClient
{
    private readonly MobileApiTransport transport;
    internal MobileReceivingOrderClient(MobileApiTransport transport) => this.transport = transport;
    private static string Route => MobileApiRoutes.ReceivingOrders;

    public Task<IReadOnlyList<MobileReceivingWorkOrderResponse>> GetOrdersAsync(
        Guid warehouseId, CancellationToken ct = default) =>
        GetListAsync<MobileReceivingWorkOrderResponse>($"{Route}?warehouseId={warehouseId:D}", ct);

    public Task<IReadOnlyList<MobileReceivingSourceResponse>> GetUnlinkedSourcesAsync(
        Guid warehouseId, CancellationToken ct = default) =>
        GetListAsync<MobileReceivingSourceResponse>(
            $"{Route}/sources/unlinked?warehouseId={warehouseId:D}", ct);

    public Task<MobileReceivingWorkOrderResponse> GetAsync(
        Guid orderId, CancellationToken ct = default) =>
        GetAsync<MobileReceivingWorkOrderResponse>($"{Route}/{orderId:D}", ct);

    public Task<MobileReceivingDocumentResolutionResponse> ResolveDocumentAsync(
        Guid warehouseId, string barcode, CancellationToken ct = default) =>
        PostAsync<MobileReceivingResolveDocumentRequest, MobileReceivingDocumentResolutionResponse>(
            $"{Route}/sources/resolve?warehouseId={warehouseId:D}",
            new MobileReceivingResolveDocumentRequest(barcode), ct);

    public Task<MobileReceivingCommandIdResponse> CreateOrderAsync(
        IReadOnlyList<Guid> sourceRefKeys, CancellationToken ct = default) =>
        PostAsync<MobileReceivingCreateOrderRequest, MobileReceivingCommandIdResponse>(
            Route, new MobileReceivingCreateOrderRequest(sourceRefKeys), ct);

    public Task<IReadOnlyList<MobileReceivingSkuCandidateResponse>> SearchSkuAsync(
        Guid orderId, string? search, bool catalog, CancellationToken ct = default) =>
        GetListAsync<MobileReceivingSkuCandidateResponse>(
            $"{Route}/{orderId:D}/skus?scope={(catalog ? "Catalog" : "Order")}&search={Uri.EscapeDataString(search ?? string.Empty)}", ct);

    public Task<MobileReceivingSkuCandidateResponse> ResolveSkuAsync(
        Guid orderId, string barcode, CancellationToken ct = default) =>
        PostAsync<MobileReceivingBarcodeRequest, MobileReceivingSkuCandidateResponse>(
            $"{Route}/{orderId:D}/skus/resolve-barcode",
            new MobileReceivingBarcodeRequest(barcode), ct);

    public Task<MobileReceivingCommandIdResponse> AddSkuAsync(
        Guid orderId, Guid skuId, Guid requestId, CancellationToken ct = default) =>
        PostIdAsync($"{Route}/{orderId:D}/skus/{skuId:D}", requestId, ct);

    public Task<MobileReceivingCommandIdResponse> ConfirmZeroAsync(
        Guid orderId, Guid skuId, Guid requestId, CancellationToken ct = default) =>
        PostIdAsync($"{Route}/{orderId:D}/skus/{skuId:D}/confirm-zero", requestId, ct);

    public Task<MobileReceivingCommandIdResponse> JoinAsync(
        Guid orderId, Guid? locationId, Guid requestId, CancellationToken ct = default) =>
        PostAsync<MobileReceivingJoinCycleRequest, MobileReceivingCommandIdResponse>(
            $"{Route}/{orderId:D}/join",
            new MobileReceivingJoinCycleRequest(requestId, locationId), ct);

    public Task<MobileReceivingCommandIdResponse> OpenLpnAsync(
        Guid orderId, string code, Guid requestId, CancellationToken ct = default) =>
        PostAsync<MobileReceivingOpenLpnRequest, MobileReceivingCommandIdResponse>(
            $"{Route}/{orderId:D}/lpns",
            new MobileReceivingOpenLpnRequest(requestId, code), ct);

    public Task<MobileReceivingCommandIdResponse> EditLpnAsync(
        Guid orderId, Guid lpnId, Guid skuId, decimal quantity,
        DateOnly? expirationDate, Guid requestId, CancellationToken ct = default) =>
        PutAsync<MobileReceivingEditLpnRequest, MobileReceivingCommandIdResponse>(
            $"{Route}/{orderId:D}/lpns/{lpnId:D}",
            new MobileReceivingEditLpnRequest(requestId, skuId, quantity, expirationDate), ct);

    public Task<MobileReceivingCommandIdResponse> CancelLpnAsync(
        Guid orderId, Guid lpnId, Guid requestId, CancellationToken ct = default) =>
        PostIdAsync($"{Route}/{orderId:D}/lpns/{lpnId:D}/cancel", requestId, ct);

    public Task<MobileReceivingCloseResultResponse> CloseLpnAsync(
        Guid orderId, Guid lpnId, bool confirmExpiredDate, string? managerReason,
        Guid requestId, CancellationToken ct = default) =>
        PostAsync<MobileReceivingCloseLpnRequest, MobileReceivingCloseResultResponse>(
            $"{Route}/{orderId:D}/lpns/{lpnId:D}/close",
            new MobileReceivingCloseLpnRequest(requestId, confirmExpiredDate, managerReason), ct);

    private Task<MobileReceivingCommandIdResponse> PostIdAsync(
        string route, Guid requestId, CancellationToken ct) =>
        PostAsync<MobileReceivingCommandRequest, MobileReceivingCommandIdResponse>(
            route, new MobileReceivingCommandRequest(requestId), ct);

    private async Task<IReadOnlyList<T>> GetListAsync<T>(string route, CancellationToken ct)
    {
        using var response = await transport.GetAsync(route, ct);
        return await response.Content.ReadFromJsonAsync<List<T>>(ct)
            ?? throw MobileApiTransport.InvalidResponse(response, "Сервер вернул некорректный список приёмки.");
    }

    private async Task<T> GetAsync<T>(string route, CancellationToken ct)
    {
        using var response = await transport.GetAsync(route, ct);
        return await response.Content.ReadFromJsonAsync<T>(ct)
            ?? throw MobileApiTransport.InvalidResponse(response, "Сервер вернул некорректное состояние приёмки.");
    }

    private async Task<TResponse> PostAsync<TRequest, TResponse>(
        string route, TRequest request, CancellationToken ct)
    {
        using var response = await transport.PostAsync(route, request, ct);
        return await MobileApiTransport.ReadCommandResponseAsync<TResponse>(
            response, "Сервер вернул некорректный результат команды приёмки.", ct);
    }

    private async Task<TResponse> PutAsync<TRequest, TResponse>(
        string route, TRequest request, CancellationToken ct)
    {
        using var response = await transport.PutAsync(route, request, ct);
        return await MobileApiTransport.ReadCommandResponseAsync<TResponse>(
            response, "Сервер вернул некорректный результат команды приёмки.", ct);
    }
}
