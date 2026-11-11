using System.Security.Claims;
using Wms.Application.Picking;
using Wms.Application.Replenishment;
using Wms.Common;
using Wms.Contracts.Mobile.V1;

namespace Wms.WebApi.Mobile;

internal static class MobileReplenishmentEndpoints
{
    public static IEndpointRouteBuilder MapMobileReplenishmentEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var assemblies = endpoints.MapGroup(MobileApiRoutes.PickingAssemblies)
            .WithTags("Mobile Picking Replenishments")
            .RequireAuthorization(MobileAuthorization.PickingWorkerPolicy);
        assemblies.MapGet("/{assemblyId:guid}/replenishments", GetAssemblyAsync)
            .WithMobileResponses<MobilePickingReplenishmentsResponse>();
        assemblies.MapPost("/{assemblyId:guid}/replenishments/order", OrderAsync)
            .WithMobileResponses<MobileReplenishmentResourceIdResponse>();

        var tasks = endpoints.MapGroup(MobileApiRoutes.ReplenishmentTasks)
            .WithTags("Mobile Replenishment Tasks")
            .RequireAuthorization(MobileAuthorization.PickingWorkerPolicy);
        tasks.MapGet("", ListAsync)
            .WithMobileResponses<IReadOnlyList<MobileReplenishmentQueueItemResponse>>();
        tasks.MapGet("/{taskId:guid}", GetTaskAsync)
            .WithMobileResponses<MobileReplenishmentTaskResponse>();
        tasks.MapPost("/{taskId:guid}/source", StartSourceAsync)
            .WithMobileResponses<MobileReplenishmentResourceIdResponse>();
        tasks.MapPost("/{taskId:guid}/transfers", TransferAsync)
            .WithMobileResponses<MobileReplenishmentResourceIdResponse>();
        tasks.MapPost("/{taskId:guid}/shortages", ReportShortageAsync)
            .WithMobileResponses<MobileReplenishmentResourceIdResponse>();
        tasks.MapPost("/{taskId:guid}/source-completion", CompleteSourceAsync)
            .WithMobileResponses<MobileReplenishmentResourceIdResponse>();
        return endpoints;
    }

    private static async Task<IResult> GetAssemblyAsync(
        Guid assemblyId, Guid warehouseId, ClaimsPrincipal principal,
        IPickingAssemblyStore assemblies, GetPickingReplenishments get,
        CancellationToken ct)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return TypedResults.Unauthorized();
        var warehouse = await CheckAssemblyWarehouseAsync(assemblies,
            assemblyId, warehouseId, ct);
        if (!warehouse.IsSuccess)
            return MobileEndpointResults.CommandProblem(warehouse.Error!);
        var result = await get.ExecuteAsync(assemblyId, userId, ct);
        if (!result.IsSuccess)
            return MobileEndpointResults.CommandProblem(result.Error!);
        var view = result.Value!;
        return TypedResults.Ok(new MobilePickingReplenishmentsResponse(
            view.PickingAssemblyId, view.Revision,
            view.Proposals.Select(x => new MobileReplenishmentProposalResponse(
                x.StockKeepingUnitId, x.SourceLocationId, x.SourceCode,
                x.DestinationLocationId, x.DestinationCode, x.Quantity,
                x.Groups.Select(group => new MobileReplenishmentProposalGroupResponse(
                    group.ExpirationDate, group.Quantity)).ToArray())).ToArray(),
            view.Deficits.Select(x => new MobileReplenishmentDeficitResponse(
                x.StockKeepingUnitId, x.Quantity)).ToArray(),
            view.Tasks.Select(x => new MobilePickingReplenishmentTaskResponse(
                x.Id, x.StockKeepingUnitId, x.OrderedQuantity,
                x.TransferredQuantity, (MobileReplenishmentTaskStatus)x.Status,
                x.RecommendedSourceLocationId, x.RecommendedSourceCode,
                x.RecommendedDestinationLocationId,
                x.RecommendedDestinationCode)).ToArray()));
    }

    private static async Task<IResult> OrderAsync(
        Guid assemblyId, MobileReplenishmentOrderRequest request,
        ClaimsPrincipal principal, IPickingAssemblyStore assemblies,
        OrderReplenishment order, CancellationToken ct)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return TypedResults.Unauthorized();
        var warehouse = await CheckAssemblyWarehouseAsync(assemblies,
            assemblyId, request.WarehouseId, ct);
        if (!warehouse.IsSuccess)
            return MobileEndpointResults.CommandProblem(warehouse.Error!);
        return ResourceId(await order.ExecuteAsync(new OrderReplenishmentRequest(
            request.ClientRequestId, assemblyId, request.ExpectedRevision,
            request.SkuId, request.SourceLocationId,
            request.ExpectedDestinationLocationId, request.Quantity, userId), ct));
    }

    private static async Task<IResult> ListAsync(
        Guid warehouseId, ClaimsPrincipal principal, GetReplenishmentTasks get,
        CancellationToken ct)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return TypedResults.Unauthorized();
        var result = await get.ExecuteAsync(warehouseId, userId, ct);
        return result.IsSuccess
            ? TypedResults.Ok<IReadOnlyList<MobileReplenishmentQueueItemResponse>>(
                result.Value!.Select(x => new MobileReplenishmentQueueItemResponse(
                    x.Id, x.PickingAssemblyId, x.AssemblyNumber,
                    x.StockKeepingUnitId, x.SkuCode, x.SkuName,
                    x.OrderedQuantity, x.TransferredQuantity,
                    (MobileReplenishmentTaskStatus)x.Status, x.AssignedTo,
                    x.OrderedAtUtc, x.PriorityQueue.ToString(),
                    x.RecommendedSourceLocationId, x.RecommendedSourceCode,
                    x.RecommendedDestinationLocationId,
                    x.RecommendedDestinationCode)).ToArray())
            : MobileEndpointResults.CommandProblem(result.Error!);
    }

    private static async Task<IResult> GetTaskAsync(
        Guid taskId, Guid warehouseId, ClaimsPrincipal principal,
        GetReplenishmentTask get, CancellationToken ct)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return TypedResults.Unauthorized();
        var result = await get.ExecuteAsync(taskId, userId, ct);
        if (!result.IsSuccess)
            return MobileEndpointResults.CommandProblem(result.Error!);
        var detail = result.Value!;
        if (detail.WarehouseId != warehouseId)
            return MobileEndpointResults.CommandProblem(OperationError.Conflict(
                "Задание относится к другому складу."));
        return TypedResults.Ok(new MobileReplenishmentTaskResponse(
            detail.Id, detail.PickingAssemblyId, detail.WarehouseId,
            detail.StockKeepingUnitId, detail.SkuName,
            detail.OrderedQuantity, detail.TransferredQuantity,
            (MobileReplenishmentTaskStatus)detail.Status,
            detail.AssignedTo, detail.OrderedAtUtc,
            detail.RecommendedSourceLocationId, detail.RecommendedSourceCode,
            detail.RecommendedDestinationLocationId,
            detail.RecommendedDestinationCode,
            detail.Cycles.Select(cycle => new MobileReplenishmentSourceCycleResponse(
                cycle.Id, cycle.SourceLocationId, cycle.SourceCode,
                cycle.StartedAtUtc, cycle.CompletedAtUtc, cycle.SourceWasEmpty,
                cycle.Transfers.Select(transfer => new MobileReplenishmentTransferResponse(
                    transfer.Id, transfer.DestinationLocationId,
                    transfer.DestinationCode, transfer.ExpirationDate,
                    transfer.Quantity, transfer.EffectiveQuantity,
                    transfer.ConfirmedAtUtc)).ToArray())).ToArray(),
            detail.ActiveSourceStock.Select(x => new MobileReplenishmentSourceStockResponse(
                x.SkuId, x.SkuName, x.ExpirationDate,
                x.AccountingQuantity, x.BlockedQuantity)).ToArray()));
    }

    private static async Task<IResult> StartSourceAsync(
        Guid taskId, MobileReplenishmentStartSourceRequest request,
        ClaimsPrincipal principal, IReplenishmentStore store,
        StartReplenishmentSource start, CancellationToken ct)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return TypedResults.Unauthorized();
        var warehouse = await CheckTaskWarehouseAsync(store, taskId,
            request.WarehouseId, ct);
        if (!warehouse.IsSuccess)
            return MobileEndpointResults.CommandProblem(warehouse.Error!);
        return ResourceId(await start.ExecuteAsync(new StartReplenishmentSourceRequest(
            request.ClientRequestId, taskId, request.SourceLocationCode,
            userId), ct));
    }

    private static async Task<IResult> TransferAsync(
        Guid taskId, MobileReplenishmentTransferRequest request,
        ClaimsPrincipal principal, IReplenishmentStore store,
        ConfirmReplenishmentTransfer transfer, CancellationToken ct)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return TypedResults.Unauthorized();
        var warehouse = await CheckTaskWarehouseAsync(store, taskId,
            request.WarehouseId, ct);
        if (!warehouse.IsSuccess)
            return MobileEndpointResults.CommandProblem(warehouse.Error!);
        return ResourceId(await transfer.ExecuteAsync(
            new ConfirmReplenishmentTransferRequest(request.ClientRequestId,
                taskId, request.SourceCycleId,
                request.DestinationLocationCode, request.SkuId,
                request.ExpirationDate, request.Quantity, userId), ct));
    }

    private static async Task<IResult> ReportShortageAsync(
        Guid taskId, MobileReplenishmentShortageRequest request,
        ClaimsPrincipal principal, IReplenishmentStore store,
        ReportReplenishmentShortage report, CancellationToken ct)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return TypedResults.Unauthorized();
        var warehouse = await CheckTaskWarehouseAsync(store, taskId,
            request.WarehouseId, ct);
        if (!warehouse.IsSuccess)
            return MobileEndpointResults.CommandProblem(warehouse.Error!);
        return ResourceId(await report.ExecuteAsync(
            new ReportReplenishmentShortageRequest(request.ClientRequestId,
                taskId, request.SourceCycleId, request.SkuId,
                request.ExpirationDate, request.ExpectedQuantity,
                request.FoundQuantity, userId, request.Comment), ct));
    }

    private static async Task<IResult> CompleteSourceAsync(
        Guid taskId, MobileReplenishmentCompleteSourceRequest request,
        ClaimsPrincipal principal, IReplenishmentStore store,
        CompleteReplenishmentSource complete, CancellationToken ct)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return TypedResults.Unauthorized();
        var warehouse = await CheckTaskWarehouseAsync(store, taskId,
            request.WarehouseId, ct);
        if (!warehouse.IsSuccess)
            return MobileEndpointResults.CommandProblem(warehouse.Error!);
        return ResourceId(await complete.ExecuteAsync(
            new CompleteReplenishmentSourceRequest(request.ClientRequestId,
                taskId, request.SourceCycleId,
                request.ScannedSourceLocationCode, request.SourceWasEmpty,
                userId), ct));
    }

    private static async Task<OperationResult> CheckAssemblyWarehouseAsync(
        IPickingAssemblyStore store, Guid assemblyId, Guid warehouseId,
        CancellationToken ct)
    {
        if (assemblyId == Guid.Empty || warehouseId == Guid.Empty)
            return OperationError.Invalid("Выберите сборку и склад.");
        var assembly = await store.ReadAssemblyAsync(assemblyId, ct);
        return assembly is null
            ? OperationError.NotFound("Сборка WMS не найдена.")
            : assembly.WarehouseId == warehouseId
                ? OperationResult.Success()
                : OperationError.Conflict("Сборка относится к другому складу.");
    }

    private static async Task<OperationResult> CheckTaskWarehouseAsync(
        IReplenishmentStore store, Guid taskId, Guid warehouseId,
        CancellationToken ct)
    {
        if (taskId == Guid.Empty || warehouseId == Guid.Empty)
            return OperationError.Invalid("Выберите задание и склад.");
        var task = await store.ReadTaskAsync(taskId, ct);
        return task is null
            ? OperationError.NotFound("Задание Подпитки не найдено.")
            : task.WarehouseId == warehouseId
                ? OperationResult.Success()
                : OperationError.Conflict("Задание относится к другому складу.");
    }

    private static IResult ResourceId(OperationResult<Guid> result) =>
        result.IsSuccess
            ? TypedResults.Ok(new MobileReplenishmentResourceIdResponse(result.Value))
            : MobileEndpointResults.CommandProblem(result.Error!);
}
