using System.Net.Http.Json;
using Wms.Contracts.Mobile.V1;

namespace Wms.Mobile.Services;

public sealed class MobileReplenishmentClient
{
    private readonly MobileApiTransport transport;

    internal MobileReplenishmentClient(MobileApiTransport transport) =>
        this.transport = transport;

    private static string TasksRoute => MobileApiRoutes.ReplenishmentTasks;
    private static string AssembliesRoute => MobileApiRoutes.PickingAssemblies;

    public Task<MobilePickingReplenishmentsResponse> AssemblyAsync(
        Guid assemblyId, Guid warehouseId, CancellationToken ct = default) =>
        GetAsync<MobilePickingReplenishmentsResponse>(
            $"{AssembliesRoute}/{assemblyId:D}/replenishments?warehouseId={warehouseId:D}", ct);

    public Task<MobileReplenishmentResourceIdResponse> OrderAsync(
        Guid assemblyId, MobileReplenishmentOrderRequest request,
        CancellationToken ct = default) =>
        PostAsync<MobileReplenishmentOrderRequest>(
            $"{AssembliesRoute}/{assemblyId:D}/replenishments/order", request, ct);

    public async Task<IReadOnlyList<MobileReplenishmentQueueItemResponse>> QueueAsync(
        Guid warehouseId, CancellationToken ct = default)
    {
        using var response = await transport.GetAsync(
            $"{TasksRoute}?warehouseId={warehouseId:D}", ct);
        return await response.Content.ReadFromJsonAsync<List<MobileReplenishmentQueueItemResponse>>(ct)
            ?? throw MobileApiTransport.InvalidResponse(response,
                "Сервер вернул некорректную очередь Подпитки.");
    }

    public Task<MobileReplenishmentTaskResponse> TaskAsync(
        Guid taskId, Guid warehouseId, CancellationToken ct = default) =>
        GetAsync<MobileReplenishmentTaskResponse>(
            $"{TasksRoute}/{taskId:D}?warehouseId={warehouseId:D}", ct);

    public Task<MobileReplenishmentResourceIdResponse> StartSourceAsync(
        Guid taskId, MobileReplenishmentStartSourceRequest request,
        CancellationToken ct = default) =>
        PostAsync<MobileReplenishmentStartSourceRequest>(
            $"{TasksRoute}/{taskId:D}/source", request, ct);

    public Task<MobileReplenishmentResourceIdResponse> TransferAsync(
        Guid taskId, MobileReplenishmentTransferRequest request,
        CancellationToken ct = default) =>
        PostAsync<MobileReplenishmentTransferRequest>(
            $"{TasksRoute}/{taskId:D}/transfers", request, ct);

    public Task<MobileReplenishmentResourceIdResponse> ReportShortageAsync(
        Guid taskId, MobileReplenishmentShortageRequest request,
        CancellationToken ct = default) =>
        PostAsync<MobileReplenishmentShortageRequest>(
            $"{TasksRoute}/{taskId:D}/shortages", request, ct);

    public Task<MobileReplenishmentResourceIdResponse> CompleteSourceAsync(
        Guid taskId, MobileReplenishmentCompleteSourceRequest request,
        CancellationToken ct = default) =>
        PostAsync<MobileReplenishmentCompleteSourceRequest>(
            $"{TasksRoute}/{taskId:D}/source-completion", request, ct);

    private async Task<T> GetAsync<T>(string route, CancellationToken ct)
    {
        using var response = await transport.GetAsync(route, ct);
        return await response.Content.ReadFromJsonAsync<T>(ct)
            ?? throw MobileApiTransport.InvalidResponse(response,
                "Сервер вернул некорректные данные Подпитки.");
    }

    private async Task<MobileReplenishmentResourceIdResponse> PostAsync<T>(
        string route, T request, CancellationToken ct)
    {
        using var response = await transport.PostAsync(route, request, ct);
        return await MobileApiTransport.ReadCommandResponseAsync<MobileReplenishmentResourceIdResponse>(
            response, "Сервер вернул некорректный результат команды Подпитки.", ct);
    }
}
