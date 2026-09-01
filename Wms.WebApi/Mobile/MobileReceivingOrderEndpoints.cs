using System.Security.Claims;
using Wms.Application.ReceivingOrders;
using Wms.Application.StorageLocations;
using Wms.Common;
using Wms.Contracts.Mobile.V1;
using Wms.Domain.Enums;

namespace Wms.WebApi.Mobile;

internal static class MobileReceivingOrderEndpoints
{
    private const int LineSearchResultLimit = 10;

    public static IEndpointRouteBuilder MapMobileReceivingOrderEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(MobileApiRoutes.ReceivingOrders)
            .WithTags("Mobile Receiving Orders")
            .RequireAuthorization(MobileAuthorization.WarehouseOperatorPolicy);

        group.MapGet("", GetWorkQueueAsync)
            .WithMobileResponses<MobileReceivingOrderWorkQueueResponse>();
        group.MapPost("/resolve-document", ResolveDocumentAsync)
            .WithMobileResponses<MobileReceivingOrderDetailsResponse>();
        group.MapGet("/{orderId:guid}", GetDetailsAsync)
            .WithMobileResponses<MobileReceivingOrderDetailsResponse>();
        group.MapPost("/{orderId:guid}/start-receiving", StartReceivingAsync)
            .WithMobileResponses<MobileReceivingOrderCommandResponse>();
        group.MapPost("/{orderId:guid}/lines/resolve-sku", ResolveSkuAsync)
            .WithMobileResponses<IReadOnlyList<MobileReceivingOrderLineCandidateResponse>>();
        group.MapGet("/{orderId:guid}/lines/search", SearchLinesAsync)
            .WithMobileResponses<MobileReceivingOrderLineSearchResponse>();
        group.MapPost("/{orderId:guid}/lines/{lineNumber:int}/scan", IncrementLineAsync)
            .WithMobileResponses<MobileReceivingOrderCommandResponse>();
        group.MapPost("/{orderId:guid}/lines/{lineNumber:int}/quantity", SetLineQuantityAsync)
            .WithMobileResponses<MobileReceivingOrderCommandResponse>();
        group.MapPost("/{orderId:guid}/complete-receiving", CompleteReceivingAsync)
            .WithMobileResponses<MobileReceivingOrderCommandResponse>();
        group.MapPost("/{orderId:guid}/start-putaway", StartPutawayAsync)
            .WithMobileResponses<MobileReceivingOrderCommandResponse>();
        group.MapPost("/{orderId:guid}/putaway-movements", AddPutawayMovementAsync)
            .WithMobileResponses<MobileReceivingOrderCommandResponse>();
        group.MapPost(
            "/{orderId:guid}/putaway-movements/{movementId:guid}/delete",
            DeletePutawayMovementAsync)
            .WithMobileResponses<MobileReceivingOrderCommandResponse>();
        group.MapPost("/{orderId:guid}/complete-putaway", CompletePutawayAsync)
            .WithMobileResponses<MobileReceivingOrderCommandResponse>();
        return endpoints;
    }

    private static async Task<IResult> GetWorkQueueAsync(
        Guid warehouseId,
        MobileReceivingOrderQueryService queryService,
        CancellationToken ct)
    {
        if (warehouseId == Guid.Empty)
        {
            return MobileEndpointResults.CommandProblem(
                OperationError.Invalid("Выберите склад."));
        }

        var queue = await queryService.GetWorkQueueAsync(warehouseId, ct);
        return TypedResults.Ok(new MobileReceivingOrderWorkQueueResponse(
            queue.Receiving.Select(MapSummary).ToList(),
            queue.Putaway.Select(MapSummary).ToList()));
    }

    private static async Task<IResult> ResolveDocumentAsync(
        MobileResolveReceivingOrderDocumentRequest request,
        MobileReceivingOrderQueryService queryService,
        CancellationToken ct)
    {
        var result = await queryService.ResolveDocumentAsync(
            request.WarehouseId,
            request.Barcode,
            ct);
        return result.IsSuccess
            ? TypedResults.Ok(MapDetails(result.Value!))
            : MobileEndpointResults.CommandProblem(result.Error!);
    }

    private static async Task<IResult> GetDetailsAsync(
        Guid orderId,
        MobileReceivingOrderQueryService queryService,
        CancellationToken ct)
    {
        var result = await queryService.GetDetailsAsync(orderId, ct);
        return result.IsSuccess
            ? TypedResults.Ok(MapDetails(result.Value!))
            : MobileEndpointResults.CommandProblem(result.Error!);
    }

    private static async Task<IResult> StartReceivingAsync(
        Guid orderId,
        MobileStartReceivingOrderRequest request,
        ClaimsPrincipal principal,
        MobileReceivingOrderQueryService queryService,
        StorageLocationQueryService locationQueryService,
        MobileReceivingOrderCommandService commandService,
        CancellationToken ct)
    {
        var userId = GetUserId(principal);
        if (userId is null)
        {
            return TypedResults.Unauthorized();
        }

        var detailsResult = await queryService.GetDetailsAsync(orderId, ct);
        if (!detailsResult.IsSuccess)
        {
            return MobileEndpointResults.CommandProblem(detailsResult.Error!);
        }

        var locationResult = await locationQueryService.ResolveBarcodeAsync(
            request.ReceivingLocationBarcode,
            detailsResult.Value!.Order.WarehouseId,
            ZoneType.Receiving,
            ct);
        if (!locationResult.IsSuccess)
        {
            return MobileEndpointResults.CommandProblem(locationResult.Error!);
        }

        var result = await commandService.StartReceivingAsync(
            orderId,
            locationResult.Value!.Id,
            request.ClientRequestId,
            userId,
            ct);
        return await CommandResultAsync(result, orderId, queryService, ct);
    }

    private static async Task<IResult> ResolveSkuAsync(
        Guid orderId,
        MobileResolveReceivingOrderSkuRequest request,
        MobileReceivingOrderQueryService queryService,
        CancellationToken ct)
    {
        var result = await queryService.ResolveLineBarcodeAsync(
            orderId,
            request.Barcode,
            ct);
        return result.IsSuccess
            ? TypedResults.Ok<IReadOnlyList<MobileReceivingOrderLineCandidateResponse>>(
                result.Value!.Select(MapCandidate).ToList())
            : MobileEndpointResults.CommandProblem(result.Error!);
    }

    private static async Task<IResult> SearchLinesAsync(
        Guid orderId,
        string? query,
        MobileReceivingOrderQueryService queryService,
        CancellationToken ct)
    {
        var result = await queryService.SearchLinesAsync(
            orderId,
            query,
            LineSearchResultLimit,
            ct);
        return result.IsSuccess
            ? TypedResults.Ok(new MobileReceivingOrderLineSearchResponse(
                result.Value!.Items.Select(MapCandidate).ToList(),
                result.Value.HasMore))
            : MobileEndpointResults.CommandProblem(result.Error!);
    }

    private static async Task<IResult> IncrementLineAsync(
        Guid orderId,
        int lineNumber,
        MobileReceivingOrderCommandRequest request,
        ClaimsPrincipal principal,
        MobileReceivingOrderCommandService commandService,
        MobileReceivingOrderQueryService queryService,
        CancellationToken ct)
    {
        var userId = GetUserId(principal);
        if (userId is null)
        {
            return TypedResults.Unauthorized();
        }

        var result = await commandService.IncrementItemFactAsync(
            orderId,
            lineNumber,
            request.ClientRequestId,
            userId,
            ct);
        return await CommandResultAsync(
            result,
            orderId,
            queryService,
            ct,
            changedLineNumber: lineNumber);
    }

    private static async Task<IResult> SetLineQuantityAsync(
        Guid orderId,
        int lineNumber,
        MobileSetReceivingOrderLineQuantityRequest request,
        ClaimsPrincipal principal,
        MobileReceivingOrderCommandService commandService,
        MobileReceivingOrderQueryService queryService,
        CancellationToken ct)
    {
        var userId = GetUserId(principal);
        if (userId is null)
        {
            return TypedResults.Unauthorized();
        }

        var result = await commandService.SetItemFactQuantityAsync(
            orderId,
            lineNumber,
            request.Quantity,
            request.ClientRequestId,
            userId,
            ct);
        return await CommandResultAsync(
            result,
            orderId,
            queryService,
            ct,
            changedLineNumber: lineNumber);
    }

    private static Task<IResult> CompleteReceivingAsync(
        Guid orderId,
        MobileReceivingOrderCommandRequest request,
        ClaimsPrincipal principal,
        MobileReceivingOrderCommandService commandService,
        MobileReceivingOrderQueryService queryService,
        CancellationToken ct) =>
        ExecuteOrderCommandAsync(
            orderId,
            request.ClientRequestId,
            principal,
            commandService.CompleteReceivingAsync,
            queryService,
            ct);

    private static Task<IResult> StartPutawayAsync(
        Guid orderId,
        MobileReceivingOrderCommandRequest request,
        ClaimsPrincipal principal,
        MobileReceivingOrderCommandService commandService,
        MobileReceivingOrderQueryService queryService,
        CancellationToken ct) =>
        ExecuteOrderCommandAsync(
            orderId,
            request.ClientRequestId,
            principal,
            commandService.StartPutawayAsync,
            queryService,
            ct);

    private static async Task<IResult> AddPutawayMovementAsync(
        Guid orderId,
        MobileAddReceivingOrderPutawayMovementRequest request,
        ClaimsPrincipal principal,
        MobileReceivingOrderQueryService queryService,
        StorageLocationQueryService locationQueryService,
        MobileReceivingOrderCommandService commandService,
        CancellationToken ct)
    {
        var userId = GetUserId(principal);
        if (userId is null)
        {
            return TypedResults.Unauthorized();
        }

        var detailsResult = await queryService.GetDetailsAsync(orderId, ct);
        if (!detailsResult.IsSuccess)
        {
            return MobileEndpointResults.CommandProblem(detailsResult.Error!);
        }

        var locationResult = await locationQueryService.ResolveBarcodeAsync(
            request.DestinationStorageLocationBarcode,
            detailsResult.Value!.Order.WarehouseId,
            ZoneType.Storage,
            ct);
        if (!locationResult.IsSuccess)
        {
            return MobileEndpointResults.CommandProblem(locationResult.Error!);
        }

        var result = await commandService.AddPutawayMovementAsync(
            orderId,
            request.LineNumber,
            locationResult.Value!.Id,
            request.Quantity,
            request.ClientRequestId,
            userId,
            ct);
        return await CommandResultAsync(
            result,
            orderId,
            queryService,
            ct,
            changedMovementId: result.IsSuccess ? result.Value : null);
    }

    private static async Task<IResult> DeletePutawayMovementAsync(
        Guid orderId,
        Guid movementId,
        MobileReceivingOrderCommandRequest request,
        ClaimsPrincipal principal,
        MobileReceivingOrderCommandService commandService,
        MobileReceivingOrderQueryService queryService,
        CancellationToken ct)
    {
        var userId = GetUserId(principal);
        if (userId is null)
        {
            return TypedResults.Unauthorized();
        }

        var result = await commandService.DeletePutawayMovementAsync(
            orderId,
            movementId,
            request.ClientRequestId,
            userId,
            ct);
        return await CommandResultAsync(
            result,
            orderId,
            queryService,
            ct,
            changedMovementId: movementId);
    }

    private static Task<IResult> CompletePutawayAsync(
        Guid orderId,
        MobileReceivingOrderCommandRequest request,
        ClaimsPrincipal principal,
        MobileReceivingOrderCommandService commandService,
        MobileReceivingOrderQueryService queryService,
        CancellationToken ct) =>
        ExecuteOrderCommandAsync(
            orderId,
            request.ClientRequestId,
            principal,
            commandService.CompletePutawayAsync,
            queryService,
            ct);

    private static async Task<IResult> ExecuteOrderCommandAsync(
        Guid orderId,
        Guid clientRequestId,
        ClaimsPrincipal principal,
        Func<Guid, Guid, string, CancellationToken, Task<OperationResult<Guid>>> command,
        MobileReceivingOrderQueryService queryService,
        CancellationToken ct)
    {
        var userId = GetUserId(principal);
        if (userId is null)
        {
            return TypedResults.Unauthorized();
        }

        var result = await command(orderId, clientRequestId, userId, ct);
        return await CommandResultAsync(result, orderId, queryService, ct);
    }

    private static async Task<IResult> CommandResultAsync(
        OperationResult<Guid> result,
        Guid orderId,
        MobileReceivingOrderQueryService queryService,
        CancellationToken ct,
        int? changedLineNumber = null,
        Guid? changedMovementId = null)
    {
        if (!result.IsSuccess)
        {
            return MobileEndpointResults.CommandProblem(result.Error!);
        }

        var detailsResult = await queryService.GetCommandResultDetailsAsync(orderId, ct);
        return detailsResult.IsSuccess
            ? TypedResults.Ok(new MobileReceivingOrderCommandResponse(
                MapDetails(detailsResult.Value!),
                changedLineNumber,
                changedMovementId))
            : MobileEndpointResults.CommandProblem(detailsResult.Error!);
    }

    private static MobileReceivingOrderDetailsResponse MapDetails(
        MobileReceivingOrderDetails details) => new(
        MapSummary(details.Order),
        details.Lines.Select(MapLine).ToList(),
        details.Movements.Select(MapMovement).ToList());

    private static MobileReceivingOrderSummaryResponse MapSummary(
        MobileReceivingOrderSummary order) => new(
        order.Id,
        order.Number,
        order.Date,
        order.WarehouseId,
        order.WarehouseName,
        order.ShipperName,
        order.Queue.GetDisplayName(),
        order.WarehouseOperation.GetDisplayName(),
        order.BusinessOperation.GetDisplayName(),
        MapStatus(order.Status),
        MapPutawayStatus(order.PutawayStatus),
        order.Comment,
        order.ReceivingLocation is null ? null : MapLocation(order.ReceivingLocation),
        new MobileReceivingOrderProgressResponse(
            order.TotalLineCount,
            order.ConfirmedLineCount,
            order.PositiveLineCount,
            order.FullyAllocatedLineCount,
            order.PlanQuantity,
            order.FactQuantity,
            order.AllocatedQuantity),
        order.StartedAtUtc,
        order.CompletedAtUtc,
        order.PutawayStartedAtUtc,
        order.PutawayCompletedAtUtc);

    private static MobileReceivingOrderLineResponse MapLine(
        MobileReceivingOrderLine line) => new(
        line.LineNumber,
        line.StockKeepingUnitId,
        line.SkuCode,
        line.SkuName,
        line.UnitOfMeasure,
        line.PlanQuantity,
        line.FactQuantity,
        line.FactQuantity is double factQuantity
            ? factQuantity - line.PlanQuantity
            : null,
        line.AllocatedQuantity,
        line.RemainingPutawayQuantity,
        line.Comment);

    private static MobileReceivingOrderMovementResponse MapMovement(
        MobileReceivingOrderMovement movement) => new(
        movement.Id,
        movement.LineNumber,
        movement.StockKeepingUnitId,
        movement.Quantity,
        MapLocation(movement.Destination),
        movement.CreatedAtUtc,
        movement.UpdatedAtUtc,
        movement.PostedAtUtc);

    private static MobileReceivingOrderLineCandidateResponse MapCandidate(
        MobileReceivingOrderLineCandidate candidate) => new(
        candidate.LineNumber,
        candidate.StockKeepingUnitId,
        candidate.SkuCode,
        candidate.SkuName,
        candidate.UnitOfMeasure,
        candidate.PlanQuantity,
        candidate.FactQuantity,
        candidate.AllocatedQuantity,
        candidate.RemainingPutawayQuantity,
        candidate.IsExactMatch);

    private static MobileReceivingOrderLocationResponse MapLocation(
        MobileReceivingOrderLocation location) => new(
        location.Id,
        location.Name,
        location.Address,
        location.ZoneId,
        location.ZoneName);

    private static MobileReceivingOrderStatus MapStatus(ReceivingOrderStatus status) =>
        status switch
        {
            ReceivingOrderStatus.ReadyForReceiving => MobileReceivingOrderStatus.ReadyForReceiving,
            ReceivingOrderStatus.InReceiving => MobileReceivingOrderStatus.InReceiving,
            ReceivingOrderStatus.ProcessingRequired => MobileReceivingOrderStatus.ProcessingRequired,
            ReceivingOrderStatus.Received => MobileReceivingOrderStatus.Received,
            _ => throw new InvalidOperationException($"Неизвестный статус приходного ордера: {status}.")
        };

    private static MobilePutawayStatus MapPutawayStatus(PutawayStatus status) =>
        status switch
        {
            PutawayStatus.Inactive => MobilePutawayStatus.Inactive,
            PutawayStatus.Pending => MobilePutawayStatus.Pending,
            PutawayStatus.InProgress => MobilePutawayStatus.InProgress,
            PutawayStatus.Completed => MobilePutawayStatus.Completed,
            _ => throw new InvalidOperationException($"Неизвестный статус размещения: {status}.")
        };

    private static string? GetUserId(ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.NameIdentifier);

}
