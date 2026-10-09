using System.Security.Claims;
using Wms.Application.Commands;
using Wms.Application.StorageLocations;
using Wms.Application.ShippingOrders;
using Wms.Common;
using Wms.Contracts.Mobile.V1;
using Wms.Domain;
using Wms.Domain.Enums;
using Wms.Integration.OneS;

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
        group.MapPost("/{orderId:guid}/start-picking", StartPickingAsync)
            .WithMobileResponses<MobileShippingOrderCommandResponse>();
        group.MapPost("/{orderId:guid}/picking-movements", AddPickingMovementAsync)
            .WithMobileResponses<MobileShippingOrderCommandResponse>();
        group.MapPost(
                "/{orderId:guid}/picking-movements/{movementId:guid}/delete",
                DeletePickingMovementAsync)
            .WithMobileResponses<MobileShippingOrderCommandResponse>();
        group.MapPost("/{orderId:guid}/complete-picking", CompletePickingAsync)
            .WithMobileResponses<MobileShippingOrderCommandResponse>();
        group.MapPost("/{orderId:guid}/ship", ShipAsync)
            .WithMobileResponses<MobileShippingOrderCommandResponse>();
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
            queue.Picking.Select(x => MapSummary(x)).ToList(),
            queue.Shipping.Select(x => MapSummary(x)).ToList()));
    }

    private static async Task<IResult> ResolveDocumentAsync(
        MobileResolveShippingOrderDocumentRequest request,
        MobileShippingOrderQueryService queryService,
        ShippingOrderSynchronizationService synchronizationService,
        CancellationToken ct)
    {
        if (request.WarehouseId == Guid.Empty)
        {
            return MobileEndpointResults.CommandProblem(
                OperationError.Invalid("Перед сканированием документа необходимо выбрать склад."));
        }

        OperationResult<Guid> decodeResult = OneSDocumentBarcodeCodec.Decode(request.Barcode);
        if (!decodeResult.IsSuccess)
        {
            return MobileEndpointResults.CommandProblem(decodeResult.Error!);
        }

        var result = await queryService.ResolveDocumentAsync(
            request.WarehouseId,
            decodeResult.Value,
            ct);
        if (!result.IsSuccess)
        {
            return MobileEndpointResults.CommandProblem(result.Error!);
        }

        var synchronizationResult = await synchronizationService.CheckAsync(
            result.Value!.Order.Id,
            ct);
        if (!synchronizationResult.IsSuccess)
        {
            return TypedResults.Ok(MapDetails(
                result.Value!,
                verificationError: synchronizationResult.Error?.Message
                    ?? "Не удалось сверить расходный ордер с 1С."));
        }

        var currentResult = await queryService.GetDetailsAsync(result.Value.Order.Id, ct);
        return currentResult.IsSuccess
            ? TypedResults.Ok(MapDetails(currentResult.Value!, synchronizationResult.Value))
            : MobileEndpointResults.CommandProblem(currentResult.Error!);
    }

    private static async Task<IResult> GetDetailsAsync(
        Guid orderId,
        MobileShippingOrderQueryService queryService,
        ShippingOrderSynchronizationService synchronizationService,
        CancellationToken ct)
    {
        var result = await queryService.GetDetailsAsync(orderId, ct);
        if (!result.IsSuccess)
        {
            return MobileEndpointResults.CommandProblem(result.Error!);
        }

        var synchronizationResult = await synchronizationService.CheckAsync(orderId, ct);
        if (!synchronizationResult.IsSuccess)
        {
            return TypedResults.Ok(MapDetails(
                result.Value!,
                verificationError: synchronizationResult.Error?.Message
                    ?? "Не удалось сверить расходный ордер с 1С."));
        }

        var currentResult = await queryService.GetDetailsAsync(orderId, ct);
        return currentResult.IsSuccess
            ? TypedResults.Ok(MapDetails(currentResult.Value!, synchronizationResult.Value))
            : MobileEndpointResults.CommandProblem(currentResult.Error!);
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

    private static async Task<IResult> StartPickingAsync(
        Guid orderId,
        MobileStartShippingOrderPickingRequest request,
        ClaimsPrincipal principal,
        MobileShippingOrderQueryService queryService,
        StorageLocationQueryService storageLocationQueryService,
        ShippingOrderCommandService commandService,
        CancellationToken ct)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
        {
            return TypedResults.Unauthorized();
        }

        var order = await queryService.GetDetailsAsync(orderId, ct);
        if (!order.IsSuccess)
            return MobileEndpointResults.CommandProblem(order.Error!);
        var location = await storageLocationQueryService.ResolveBarcodeAsync(
            request.ShippingLocationBarcode, order.Value!.Order.WarehouseId, ZoneType.Shipping, ct);
        if (!location.IsSuccess)
            return MobileEndpointResults.CommandProblem(location.Error!);
        var shippingLocationId = location.Value!.Id;

        var result = await commandService.StartPickingAsync(
            new StartPickingCommand(orderId, shippingLocationId),
            new CommandContext(request.ClientRequestId, userId),
            ct);
        return await CommandResultAsync(result, orderId, queryService, ct);
    }

    private static async Task<IResult> AddPickingMovementAsync(
        Guid orderId,
        MobileAddShippingOrderPickingMovementRequest request,
        ClaimsPrincipal principal,
        MobileShippingOrderQueryService queryService,
        StorageLocationQueryService storageLocationQueryService,
        PickingCommandService commandService,
        CancellationToken ct)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
        {
            return TypedResults.Unauthorized();
        }

        var order = await queryService.GetDetailsAsync(orderId, ct);
        if (!order.IsSuccess)
            return MobileEndpointResults.CommandProblem(order.Error!);
        var location = await storageLocationQueryService.ResolveBarcodeAsync(
            request.SourceStorageLocationBarcode, order.Value!.Order.WarehouseId, ZoneType.Storage, ct);
        if (!location.IsSuccess)
            return MobileEndpointResults.CommandProblem(location.Error!);
        var sourceStorageLocationId = location.Value!.Id;

        var result = await commandService.AddPickingMovementAsync(
            new AddPickingMovementCommand(orderId, request.LineNumber, sourceStorageLocationId, request.Quantity),
            new CommandContext(request.ClientRequestId, userId),
            ct);
        return await CommandResultAsync(
            result,
            orderId,
            queryService,
            ct,
            changedLineNumber: request.LineNumber,
            changedMovementId: result.IsSuccess ? result.Value : null);
    }

    private static async Task<IResult> DeletePickingMovementAsync(
        Guid orderId,
        Guid movementId,
        MobileShippingOrderCommandRequest request,
        ClaimsPrincipal principal,
        MobileShippingOrderQueryService queryService,
        PickingCommandService commandService,
        CancellationToken ct)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
        {
            return TypedResults.Unauthorized();
        }

        var result = await commandService.DeletePickingMovementAsync(
            new DeletePickingMovementCommand(orderId, movementId),
            new CommandContext(request.ClientRequestId, userId),
            ct);
        return await CommandResultAsync(
            result,
            orderId,
            queryService,
            ct,
            changedMovementId: movementId);
    }

    private static async Task<IResult> CompletePickingAsync(
        Guid orderId,
        MobileShippingOrderCommandRequest request,
        ClaimsPrincipal principal,
        MobileShippingOrderQueryService queryService,
        ShippingOrderCommandService commandService,
        CancellationToken ct)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
        {
            return TypedResults.Unauthorized();
        }

        var result = await commandService.SetReadyForShipmentAsync(
            orderId,
            new CommandContext(request.ClientRequestId, userId),
            ct);
        return await CommandResultAsync(result, orderId, queryService, ct);
    }

    private static async Task<IResult> ShipAsync(
        Guid orderId,
        MobileShippingOrderCommandRequest request,
        ClaimsPrincipal principal,
        MobileShippingOrderQueryService queryService,
        ShippingOrderCommandService commandService,
        CancellationToken ct)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
        {
            return TypedResults.Unauthorized();
        }

        var result = await commandService.SetShippedAsync(
            orderId,
            new CommandContext(request.ClientRequestId, userId),
            ct);
        return await CommandResultAsync(result, orderId, queryService, ct);
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
        MobileShippingOrderDetails details,
        OrderSynchronizationAssessment? assessment = null,
        string? verificationError = null) => new(
        MapSummary(details.Order, assessment, verificationError),
        details.Lines.Select(MapLine).ToList(),
        details.Movements.Select(MapMovement).ToList());

    private static async Task<IResult> CommandResultAsync(
        OperationResult<Guid> result,
        Guid orderId,
        MobileShippingOrderQueryService queryService,
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
            ? TypedResults.Ok(new MobileShippingOrderCommandResponse(
                MapDetails(detailsResult.Value!),
                changedLineNumber,
                changedMovementId))
            : MobileEndpointResults.CommandProblem(detailsResult.Error!);
    }

    private static MobileShippingOrderSummaryResponse MapSummary(
        MobileShippingOrderSummary order,
        OrderSynchronizationAssessment? assessment = null,
        string? verificationError = null) => new(
        order.Id,
        order.Number,
        order.Date,
        order.WarehouseId,
        order.WarehouseName,
        order.ReceiverName,
        order.Queue.GetDisplayName(),
        order.WarehouseOperation.GetDisplayName(),
        MapStatus(order.Status),
        MapSynchronization(order.SynchronizationLevel, assessment, verificationError),
        order.Comment,
        order.PlannedShippingDate,
        order.DeliveryDirection,
        order.ShippingLocation is null ? null : MapLocation(order.ShippingLocation),
        new MobileShippingOrderProgressResponse(
            order.TotalLineCount,
            order.FullyPickedLineCount,
            order.PartiallyPickedLineCount,
            order.ZeroPickedLineCount),
        order.PickingStartedAtUtc,
        order.ReadyForShipmentAtUtc,
        order.ShippedAtUtc);

    private static MobileOrderSynchronizationResponse MapSynchronization(
        OrderSynchronizationLevel persistedLevel,
        OrderSynchronizationAssessment? assessment,
        string? verificationError = null)
    {
        var commentDifference = assessment?.Differences
            .LastOrDefault(x => x.FieldCode == "comment");
        return new MobileOrderSynchronizationResponse(
            MapSynchronizationLevel(assessment?.Level ?? persistedLevel),
            assessment is not null,
            assessment?.Differences.Select(x => x.FieldName).Distinct().ToList() ?? [],
            commentDifference is not null,
            commentDifference?.OneCValue,
            verificationError);
    }

    private static MobileOrderSynchronizationLevel MapSynchronizationLevel(
        OrderSynchronizationLevel level) => level switch
        {
            OrderSynchronizationLevel.Synchronized => MobileOrderSynchronizationLevel.Synchronized,
            OrderSynchronizationLevel.RequiresOperatorDecision => MobileOrderSynchronizationLevel.RequiresOperatorDecision,
            OrderSynchronizationLevel.Blocking => MobileOrderSynchronizationLevel.Blocking,
            _ => throw new InvalidOperationException($"Неизвестный уровень синхронизации: {level}.")
        };

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

}
