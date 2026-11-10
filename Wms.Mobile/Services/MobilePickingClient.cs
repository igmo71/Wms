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

    private async Task<TResponse> PostAsync<TRequest, TResponse>(
        string route, TRequest request, CancellationToken ct)
    {
        using var response = await transport.PostAsync(route, request, ct);
        return await MobileApiTransport.ReadCommandResponseAsync<TResponse>(
            response, "Сервер вернул некорректный результат команды Отбора.", ct);
    }
}
