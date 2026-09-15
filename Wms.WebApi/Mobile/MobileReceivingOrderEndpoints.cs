using System.Security.Claims;
using Wms.Application.Commands;
using Wms.Application.ReceivingOrders;
using Wms.Common;
using Wms.Contracts.Mobile.V1;
using Wms.Domain;
using Wms.Domain.Enums;
using Wms.Integration.OneS;

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
        group.MapPost("/{orderId:guid}/join-receiving", JoinReceivingAsync)
            .WithMobileResponses<MobileReceivingOrderCommandResponse>();
        return endpoints;
    }

    private static async Task<IResult> GetWorkQueueAsync(
        Guid warehouseId,
        ClaimsPrincipal principal,
        MobileReceivingOrderQueryService queryService,
        CancellationToken ct)
    {
        if (warehouseId == Guid.Empty)
        {
            return MobileEndpointResults.CommandProblem(
                OperationError.Invalid("Выберите склад."));
        }

        var userId = GetUserId(principal);
        if (userId is null)
            return TypedResults.Unauthorized();

        var queue = await queryService.GetWorkQueueAsync(warehouseId, userId, ct);
        return TypedResults.Ok(new MobileReceivingOrderWorkQueueResponse(
            queue.Personal.Select(x => MapSummary(x)).ToList(),
            queue.Available.Select(x => MapSummary(x)).ToList()));
    }

    private static async Task<IResult> ResolveDocumentAsync(
        MobileResolveReceivingOrderDocumentRequest request,
        ClaimsPrincipal principal,
        MobileReceivingOrderQueryService queryService,
        ReceivingOrderSynchronizationService synchronizationService,
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

        var userId = GetUserId(principal);
        if (userId is null)
            return TypedResults.Unauthorized();

        var result = await queryService.ResolveDocumentAsync(
            request.WarehouseId,
            decodeResult.Value,
            userId,
            ct);
        if (!result.IsSuccess)
        {
            return MobileEndpointResults.CommandProblem(result.Error!);
        }

        if (result.Value!.Order.Status == ReceivingOrderStatus.Received)
        {
            return TypedResults.Ok(MapDetails(result.Value));
        }

        var synchronizationResult = await synchronizationService.CheckAsync(
            result.Value!.Order.Id,
            ct);
        if (!synchronizationResult.IsSuccess)
        {
            return TypedResults.Ok(MapDetails(
                result.Value!,
                verificationError: synchronizationResult.Error?.Message
                    ?? "Не удалось сверить приходный ордер с 1С."));
        }

        var currentResult = await queryService.GetDetailsAsync(result.Value.Order.Id, userId, ct);
        return currentResult.IsSuccess
            ? TypedResults.Ok(MapDetails(currentResult.Value!, synchronizationResult.Value))
            : MobileEndpointResults.CommandProblem(currentResult.Error!);
    }

    private static async Task<IResult> GetDetailsAsync(
        Guid orderId,
        ClaimsPrincipal principal,
        MobileReceivingOrderQueryService queryService,
        ReceivingOrderSynchronizationService synchronizationService,
        CancellationToken ct)
    {
        var userId = GetUserId(principal);
        if (userId is null)
            return TypedResults.Unauthorized();

        var result = await queryService.GetDetailsAsync(orderId, userId, ct);
        if (!result.IsSuccess)
        {
            return MobileEndpointResults.CommandProblem(result.Error!);
        }

        if (result.Value!.Order.Status == ReceivingOrderStatus.Received)
        {
            return TypedResults.Ok(MapDetails(result.Value));
        }

        var synchronizationResult = await synchronizationService.CheckAsync(orderId, ct);
        if (!synchronizationResult.IsSuccess)
        {
            return TypedResults.Ok(MapDetails(
                result.Value!,
                verificationError: synchronizationResult.Error?.Message
                    ?? "Не удалось сверить приходный ордер с 1С."));
        }

        var currentResult = await queryService.GetDetailsAsync(orderId, userId, ct);
        return currentResult.IsSuccess
            ? TypedResults.Ok(MapDetails(currentResult.Value!, synchronizationResult.Value))
            : MobileEndpointResults.CommandProblem(currentResult.Error!);
    }

    private static async Task<IResult> JoinReceivingAsync(
        Guid orderId,
        MobileJoinReceivingOrderRequest request,
        ClaimsPrincipal principal,
        MobileReceivingOrderQueryService queryService,
        ReceivingOrderCommandService commandService,
        CancellationToken ct)
    {
        var userId = GetUserId(principal);
        if (userId is null)
        {
            return TypedResults.Unauthorized();
        }

        Guid? receivingLocationId = null;
        if (!string.IsNullOrWhiteSpace(request.ReceivingLocationBarcode))
        {
            if (!StorageLocation.TryParseBarcode(
                request.ReceivingLocationBarcode,
                out var parsedLocationId))
            {
                return MobileEndpointResults.CommandProblem(
                    OperationError.Invalid("Некорректный QR-код ячейки."));
            }
            receivingLocationId = parsedLocationId;
        }

        var result = await commandService.JoinReceivingAsync(
            new JoinReceivingOrderCommand(orderId, receivingLocationId),
            new CommandContext(request.ClientRequestId, userId),
            ct);
        return await CommandResultAsync(result, orderId, userId, queryService, ct);
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
        ReceivingOrderCommandService commandService,
        MobileReceivingOrderQueryService queryService,
        CancellationToken ct)
    {
        var userId = GetUserId(principal);
        if (userId is null)
        {
            return TypedResults.Unauthorized();
        }

        var result = await commandService.IncrementItemFactAsync(
            new IncrementReceivingFactCommand(orderId, lineNumber),
            new CommandContext(request.ClientRequestId, userId),
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
        ReceivingOrderCommandService commandService,
        MobileReceivingOrderQueryService queryService,
        CancellationToken ct)
    {
        var userId = GetUserId(principal);
        if (userId is null)
        {
            return TypedResults.Unauthorized();
        }

        var result = await commandService.SetItemFactQuantityAsync(
            new SetReceivingFactCommand(orderId, lineNumber, request.Quantity),
            new CommandContext(request.ClientRequestId, userId),
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
        ReceivingOrderCommandService commandService,
        MobileReceivingOrderQueryService queryService,
        CancellationToken ct) =>
        ExecuteOrderCommandAsync(
            orderId,
            request.ClientRequestId,
            principal,
            (id, requestId, userId, token) => commandService.CompleteReceivingAsync(
                new CompleteReceivingCommand(id, null),
                new CommandContext(requestId, userId),
                token),
            queryService,
            ct);

    private static Task<IResult> StartPutawayAsync(
        Guid orderId,
        MobileReceivingOrderCommandRequest request,
        ClaimsPrincipal principal,
        PutawayCommandService commandService,
        MobileReceivingOrderQueryService queryService,
        CancellationToken ct) =>
        ExecuteOrderCommandAsync(
            orderId,
            request.ClientRequestId,
            principal,
            (id, requestId, userId, token) => commandService.StartAsync(id, new CommandContext(requestId, userId), token),
            queryService,
            ct);

    private static async Task<IResult> AddPutawayMovementAsync(
        Guid orderId,
        MobileAddReceivingOrderPutawayMovementRequest request,
        ClaimsPrincipal principal,
        MobileReceivingOrderQueryService queryService,
        PutawayCommandService commandService,
        CancellationToken ct)
    {
        var userId = GetUserId(principal);
        if (userId is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!StorageLocation.TryParseBarcode(
            request.DestinationStorageLocationBarcode,
            out var destinationStorageLocationId))
        {
            return MobileEndpointResults.CommandProblem(
                OperationError.Invalid("Некорректный QR-код ячейки."));
        }

        var result = await commandService.AddMovementAsync(
            new AddPutawayMovementCommand(orderId, request.LineNumber, destinationStorageLocationId, request.Quantity),
            new CommandContext(request.ClientRequestId, userId),
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
        PutawayCommandService commandService,
        MobileReceivingOrderQueryService queryService,
        CancellationToken ct)
    {
        var userId = GetUserId(principal);
        if (userId is null)
        {
            return TypedResults.Unauthorized();
        }

        var result = await commandService.DeleteMovementAsync(
            new DeletePutawayMovementCommand(orderId, movementId),
            new CommandContext(request.ClientRequestId, userId),
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
        PutawayCommandService commandService,
        MobileReceivingOrderQueryService queryService,
        CancellationToken ct) =>
        ExecuteOrderCommandAsync(
            orderId,
            request.ClientRequestId,
            principal,
            (id, requestId, userId, token) => commandService.CompleteAsync(id, new CommandContext(requestId, userId), token),
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
        string userId,
        MobileReceivingOrderQueryService queryService,
        CancellationToken ct)
    {
        if (!result.IsSuccess)
            return MobileEndpointResults.CommandProblem(result.Error!);

        var detailsResult = await queryService.GetCommandResultDetailsAsync(orderId, userId, ct);
        return detailsResult.IsSuccess
            ? TypedResults.Ok(new MobileReceivingOrderCommandResponse(
                MapDetails(detailsResult.Value!)))
            : MobileEndpointResults.CommandProblem(detailsResult.Error!);
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
        MobileReceivingOrderDetails details,
        OrderSynchronizationAssessment? assessment = null,
        string? verificationError = null) => new(
        MapSummary(details.Order, assessment, verificationError),
        details.Lines.Select(MapLine).ToList(),
        details.Movements.Select(MapMovement).ToList());

    private static MobileReceivingOrderSummaryResponse MapSummary(
        MobileReceivingOrderSummary order,
        OrderSynchronizationAssessment? assessment = null,
        string? verificationError = null) => new(
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
        MapSynchronization(order.SynchronizationLevel, assessment, verificationError),
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
        order.PutawayCompletedAtUtc,
        order.IsParticipant,
        order.CanJoin
            && string.IsNullOrWhiteSpace(verificationError)
            && (assessment is null || assessment.Level == OrderSynchronizationLevel.Synchronized),
        verificationError
            ?? (assessment is { Level: not OrderSynchronizationLevel.Synchronized }
                ? "Ордер заблокирован расхождениями с 1С."
                : order.JoinBlockedReason));

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

    private static MobileReceivingOrderLineResponse MapLine(
        MobileReceivingOrderLine line) => new(
        line.LineNumber,
        line.StockKeepingUnitId,
        line.SkuCode,
        line.SkuName,
        line.UnitOfMeasure,
        line.PlanQuantity,
        line.FactQuantity,
        line.FactQuantity is decimal factQuantity
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
            ReceivingOrderStatus.Unknown => MobileReceivingOrderStatus.Unknown,
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
