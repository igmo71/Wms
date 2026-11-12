using System.Net.Http.Json;
using Wms.Contracts.Mobile.V1;

namespace Wms.Mobile.Services;

public sealed class MobilePickingClient
{
    private readonly MobileApiTransport transport;

    internal MobilePickingClient(MobileApiTransport transport) => this.transport = transport;

    private static string Route => MobileApiRoutes.PickingAssemblies;

    public async Task<IReadOnlyList<MobilePickingAssemblyListResponse>> ListAsync(
        Guid warehouseId, CancellationToken ct = default)
    {
        using var response = await transport.GetAsync(
            $"{Route}?warehouseId={warehouseId:D}", ct);
        return await response.Content.ReadFromJsonAsync<List<MobilePickingAssemblyListResponse>>(ct)
            ?? throw MobileApiTransport.InvalidResponse(response,
                "Сервер вернул некорректный список сборок.");
    }

    public async Task<MobilePickingAssemblyResponse> GetAsync(
        Guid assemblyId, Guid warehouseId, CancellationToken ct = default)
    {
        using var response = await transport.GetAsync(
            $"{Route}/{assemblyId:D}?warehouseId={warehouseId:D}", ct);
        return await response.Content.ReadFromJsonAsync<MobilePickingAssemblyResponse>(ct)
            ?? throw MobileApiTransport.InvalidResponse(response,
                "Сервер вернул некорректное состояние сборки.");
    }

    public Task<MobilePickingSourceResolutionResponse> ResolveAsync(
        Guid warehouseId, string barcode, CancellationToken ct = default) =>
        PostAsync<MobilePickingResolveSourceRequest, MobilePickingSourceResolutionResponse>(
            $"{Route}/sources/resolve?warehouseId={warehouseId:D}",
            new MobilePickingResolveSourceRequest(barcode), ct);

    public Task<MobilePickingCommandIdResponse> CreateAsync(
        Guid warehouseId, string barcode, Guid requestId,
        CancellationToken ct = default) =>
        PostAsync<MobilePickingCreateRequest, MobilePickingCommandIdResponse>(
            Route, new MobilePickingCreateRequest(requestId, warehouseId, barcode), ct);

    public Task<MobilePickingCommandIdResponse> AttachAsync(
        Guid assemblyId, Guid warehouseId, string barcode, Guid requestId,
        CancellationToken ct = default) =>
        PostAsync<MobilePickingAttachSourceRequest, MobilePickingCommandIdResponse>(
            $"{Route}/{assemblyId:D}/sources",
            new MobilePickingAttachSourceRequest(requestId, warehouseId, barcode), ct);

    public async Task<MobilePickingCommandIdResponse> DetachAsync(
        Guid assemblyId, Guid sourceRefKey, Guid warehouseId, Guid requestId,
        CancellationToken ct = default)
    {
        using var response = await transport.DeleteAsync(
            $"{Route}/{assemblyId:D}/sources/{sourceRefKey:D}",
            new MobilePickingCommandRequest(requestId, warehouseId), ct);
        return await MobileApiTransport.ReadCommandResponseAsync<MobilePickingCommandIdResponse>(
            response, "Сервер вернул некорректный результат открепления.", ct);
    }

    public Task<MobilePickingStartResponse> StartAsync(
        Guid assemblyId, Guid warehouseId, Guid requestId,
        CancellationToken ct = default) =>
        PostAsync<MobilePickingCommandRequest, MobilePickingStartResponse>(
            $"{Route}/{assemblyId:D}/start",
            new MobilePickingCommandRequest(requestId, warehouseId), ct);

    public Task<MobilePickingCommandIdResponse> TakeOverAsync(
        Guid assemblyId, Guid warehouseId, long expectedRevision, Guid requestId,
        CancellationToken ct = default) =>
        PostAsync<MobilePickingTakeOverRequest, MobilePickingCommandIdResponse>(
            $"{Route}/{assemblyId:D}/take-over",
            new MobilePickingTakeOverRequest(requestId, warehouseId, expectedRevision), ct);

    public Task<MobilePickingWorkResponse> WorkAsync(
        Guid assemblyId, Guid warehouseId, CancellationToken ct = default) =>
        GetAsync<MobilePickingWorkResponse>(
            $"{Route}/{assemblyId:D}/work?warehouseId={warehouseId:D}", ct);

    public Task<MobilePickingRouteResponse> RouteAsync(
        Guid assemblyId, Guid warehouseId, CancellationToken ct = default) =>
        GetAsync<MobilePickingRouteResponse>(
            $"{Route}/{assemblyId:D}/route?warehouseId={warehouseId:D}", ct);

    public Task<MobilePickingSkuResponse> SkuAsync(
        Guid assemblyId, Guid warehouseId, Guid skuId,
        CancellationToken ct = default) =>
        GetAsync<MobilePickingSkuResponse>(
            $"{Route}/{assemblyId:D}/skus/{skuId:D}?warehouseId={warehouseId:D}", ct);

    public Task<MobilePickingScannedSourceResponse> ScanSourceAsync(
        Guid assemblyId, MobilePickingScanSourceRequest request,
        CancellationToken ct = default) =>
        PostAsync<MobilePickingScanSourceRequest, MobilePickingScannedSourceResponse>(
            $"{Route}/{assemblyId:D}/source/scan", request, ct);

    public Task<MobilePickingResourceIdResponse> HoldPalletAsync(
        Guid assemblyId, MobilePickingHoldPalletRequest request,
        CancellationToken ct = default) =>
        PostAsync<MobilePickingHoldPalletRequest, MobilePickingResourceIdResponse>(
            $"{Route}/{assemblyId:D}/pallets/hold", request, ct);

    public Task<MobilePickingResourceIdResponse> ReturnPalletAsync(
        Guid assemblyId, MobilePickingReturnPalletRequest request,
        CancellationToken ct = default) =>
        PostAsync<MobilePickingReturnPalletRequest, MobilePickingResourceIdResponse>(
            $"{Route}/{assemblyId:D}/pallets/return", request, ct);

    public Task<MobilePickingResourceIdResponse> ConfirmPickAsync(
        Guid assemblyId, MobilePickingConfirmRequest request,
        CancellationToken ct = default) =>
        PostAsync<MobilePickingConfirmRequest, MobilePickingResourceIdResponse>(
            $"{Route}/{assemblyId:D}/picks", request, ct);

    public Task<MobilePickingResourceIdResponse> ReducePickAsync(
        Guid assemblyId, Guid entryId, MobilePickingReduceRequest request,
        CancellationToken ct = default) =>
        PostAsync<MobilePickingReduceRequest, MobilePickingResourceIdResponse>(
            $"{Route}/{assemblyId:D}/picks/{entryId:D}/return", request, ct);

    public Task<MobilePickingResourceIdResponse> ReportShortageAsync(
        Guid assemblyId, MobilePickingShortageRequest request,
        CancellationToken ct = default) =>
        PostAsync<MobilePickingShortageRequest, MobilePickingResourceIdResponse>(
            $"{Route}/{assemblyId:D}/shortages", request, ct);

    public Task<MobilePickingClosureCheckResponse> CheckClosureAsync(
        Guid assemblyId, Guid warehouseId, CancellationToken ct = default) =>
        GetAsync<MobilePickingClosureCheckResponse>(
            $"{Route}/{assemblyId:D}/closure-check?warehouseId={warehouseId:D}", ct);

    public Task<MobilePickingCloseResponse> CloseAsync(
        Guid assemblyId, MobilePickingCloseRequest request,
        CancellationToken ct = default) =>
        PostAsync<MobilePickingCloseRequest, MobilePickingCloseResponse>(
            $"{Route}/{assemblyId:D}/close", request, ct);

    private async Task<TResponse> GetAsync<TResponse>(string route, CancellationToken ct)
    {
        using var response = await transport.GetAsync(route, ct);
        return await response.Content.ReadFromJsonAsync<TResponse>(ct)
            ?? throw MobileApiTransport.InvalidResponse(response,
                "Сервер вернул некорректные данные Отбора.");
    }

    private async Task<TResponse> PostAsync<TRequest, TResponse>(
        string route, TRequest request, CancellationToken ct)
    {
        using var response = await transport.PostAsync(route, request, ct);
        return await MobileApiTransport.ReadCommandResponseAsync<TResponse>(
            response, "Сервер вернул некорректный результат команды Отбора.", ct);
    }
}
