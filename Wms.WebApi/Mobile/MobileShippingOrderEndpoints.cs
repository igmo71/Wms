using Wms.Application.ShippingOrders;
using Wms.Common;
using Wms.Contracts.Mobile.V1;
using Wms.Domain.Enums;

namespace Wms.WebApi.Mobile;

internal static class MobileShippingOrderEndpoints
{
    private const int LineSearchResultLimit = 10;

    public static IEndpointRouteBuilder MapMobileShippingOrderEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(MobileApiRoutes.ShippingOrders)
            .WithTags("Mobile Shipping Orders")
            .RequireAuthorization(MobileAuthorization.WarehouseOperatorPolicy);

        group.MapGet("", GetWorkQueueAsync)
            .WithMobileResponses<MobileShippingOrderWorkQueueResponse>();
        group.MapPost("/resolve-document", ResolveDocumentAsync)
            .WithMobileResponses<MobileShippingOrderDetailsResponse>();
        group.MapGet("/{orderId:guid}", GetDetailsAsync)
            .WithMobileResponses<MobileShippingOrderDetailsResponse>();
        group.MapPost("/{orderId:guid}/lines/resolve-sku", ResolveSkuAsync)
            .WithMobileResponses<IReadOnlyList<MobileShippingOrderLineCandidateResponse>>();
        group.MapGet("/{orderId:guid}/lines/search", SearchLinesAsync)
            .WithMobileResponses<MobileShippingOrderLineSearchResponse>();
        group.MapGet("/{orderId:guid}/lines/{lineNumber:int}/sources", GetSourcesAsync)
            .WithMobileResponses<IReadOnlyList<MobileShippingOrderSourceAvailabilityResponse>>();

        return endpoints;
    }

    private static async Task<IResult> GetWorkQueueAsync(
        Guid warehouseId,
        MobileShippingOrderQueryService queryService,
        CancellationToken ct)
    {
        if (warehouseId == Guid.Empty)
        {
            return MobileEndpointResults.CommandProblem(
                OperationError.Invalid("Выберите склад."));
        }

        var queue = await queryService.GetWorkQueueAsync(warehouseId, ct);
        return TypedResults.Ok(new MobileShippingOrderWorkQueueResponse(
            queue.Picking.Select(MapSummary).ToList(),
            queue.Shipping.Select(MapSummary).ToList()));
    }

    private static async Task<IResult> ResolveDocumentAsync(
        MobileResolveShippingOrderDocumentRequest request,
        MobileShippingOrderQueryService queryService,
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
        MobileShippingOrderQueryService queryService,
        CancellationToken ct)
    {
        var result = await queryService.GetDetailsAsync(orderId, ct);
        return result.IsSuccess
            ? TypedResults.Ok(MapDetails(result.Value!))
            : MobileEndpointResults.CommandProblem(result.Error!);
    }

    private static async Task<IResult> ResolveSkuAsync(
        Guid orderId,
        MobileResolveShippingOrderSkuRequest request,
        MobileShippingOrderQueryService queryService,
        CancellationToken ct)
    {
        var result = await queryService.ResolveLineBarcodeAsync(
            orderId,
            request.Barcode,
            ct);
        return result.IsSuccess
            ? TypedResults.Ok<IReadOnlyList<MobileShippingOrderLineCandidateResponse>>(
                result.Value!.Select(MapCandidate).ToList())
            : MobileEndpointResults.CommandProblem(result.Error!);
    }

    private static async Task<IResult> SearchLinesAsync(
        Guid orderId,
        string? query,
        MobileShippingOrderQueryService queryService,
        CancellationToken ct)
    {
        var result = await queryService.SearchLinesAsync(
            orderId,
            query,
            LineSearchResultLimit,
            ct);
        return result.IsSuccess
            ? TypedResults.Ok(new MobileShippingOrderLineSearchResponse(
                result.Value!.Items.Select(MapCandidate).ToList(),
                result.Value.HasMore))
            : MobileEndpointResults.CommandProblem(result.Error!);
    }

    private static async Task<IResult> GetSourcesAsync(
        Guid orderId,
        int lineNumber,
        MobileShippingOrderQueryService queryService,
        CancellationToken ct)
    {
        var result = await queryService.GetAvailableSourcesAsync(
            orderId,
            lineNumber,
            ct);
        return result.IsSuccess
            ? TypedResults.Ok<IReadOnlyList<MobileShippingOrderSourceAvailabilityResponse>>(
                result.Value!.Select(MapSource).ToList())
            : MobileEndpointResults.CommandProblem(result.Error!);
    }

    private static MobileShippingOrderDetailsResponse MapDetails(
        MobileShippingOrderDetails details) => new(
        MapSummary(details.Order),
        details.Lines.Select(MapLine).ToList(),
        details.Movements.Select(MapMovement).ToList());

    private static MobileShippingOrderSummaryResponse MapSummary(
        MobileShippingOrderSummary order) => new(
        order.Id,
        order.Number,
        order.Date,
        order.WarehouseId,
        order.WarehouseName,
        order.ReceiverName,
        order.Queue.GetDisplayName(),
        order.WarehouseOperation.GetDisplayName(),
        MapStatus(order.Status),
        order.Comment,
        order.PlannedShippingDate,
        order.DeliveryDirection,
        order.ShippingLocation is null ? null : MapLocation(order.ShippingLocation),
        new MobileShippingOrderProgressResponse(
            order.TotalLineCount,
            order.FullyPickedLineCount,
            order.PartiallyPickedLineCount,
            order.ZeroPickedLineCount,
            order.PlanQuantity,
            order.FactQuantity),
        order.PickingStartedAtUtc,
        order.ReadyForShipmentAtUtc,
        order.ShippedAtUtc);

    private static MobileShippingOrderLineResponse MapLine(
        MobileShippingOrderLine line) => new(
        line.LineNumber,
        line.StockKeepingUnitId,
        line.SkuCode,
        line.SkuName,
        line.UnitOfMeasure,
        line.PlanQuantity,
        line.FactQuantity,
        Math.Max(0, line.PlanQuantity - line.FactQuantity),
        line.Comment);

    private static MobileShippingOrderMovementResponse MapMovement(
        MobileShippingOrderMovement movement) => new(
        movement.Id,
        movement.LineNumber,
        movement.StockKeepingUnitId,
        movement.Quantity,
        MapLocation(movement.Source),
        movement.CreatedAtUtc,
        movement.UpdatedAtUtc,
        movement.PostedAtUtc);

    private static MobileShippingOrderLineCandidateResponse MapCandidate(
        MobileShippingOrderLineCandidate candidate) => new(
        candidate.LineNumber,
        candidate.StockKeepingUnitId,
        candidate.SkuCode,
        candidate.SkuName,
        candidate.UnitOfMeasure,
        candidate.PlanQuantity,
        candidate.FactQuantity,
        Math.Max(0, candidate.PlanQuantity - candidate.FactQuantity),
        candidate.IsExactMatch);

    private static MobileShippingOrderSourceAvailabilityResponse MapSource(
        MobileShippingOrderSourceAvailability source) => new(
        MapLocation(source.Source),
        source.PhysicalQuantity,
        source.DraftQuantity,
        Math.Max(0, source.PhysicalQuantity - source.DraftQuantity));

    private static MobileShippingOrderLocationResponse MapLocation(
        MobileShippingOrderLocation location) => new(
        location.Id,
        location.Name,
        location.Address,
        location.ZoneId,
        location.ZoneName);

    private static MobileShippingOrderStatus MapStatus(ShippingOrderStatus status) =>
        status switch
        {
            ShippingOrderStatus.Prepared => MobileShippingOrderStatus.Prepared,
            ShippingOrderStatus.ReadyForPicking => MobileShippingOrderStatus.ReadyForPicking,
            ShippingOrderStatus.ReadyForVerification => MobileShippingOrderStatus.ReadyForVerification,
            ShippingOrderStatus.InVerification => MobileShippingOrderStatus.InVerification,
            ShippingOrderStatus.Verified => MobileShippingOrderStatus.Verified,
            ShippingOrderStatus.ReadyForShipment => MobileShippingOrderStatus.ReadyForShipment,
            ShippingOrderStatus.Shipped => MobileShippingOrderStatus.Shipped,
            _ => throw new InvalidOperationException(
                $"Неизвестный статус расходного ордера: {status}.")
        };

    private static RouteHandlerBuilder WithMobileResponses<TResponse>(
        this RouteHandlerBuilder builder) =>
        builder
            .Produces<TResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces<MobileProblemResponse>(StatusCodes.Status400BadRequest)
            .Produces<MobileProblemResponse>(StatusCodes.Status404NotFound)
            .Produces<MobileProblemResponse>(StatusCodes.Status409Conflict)
            .Produces<MobileProblemResponse>(StatusCodes.Status422UnprocessableEntity);
}
