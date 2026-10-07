using System.Security.Claims;
using Wms.Application.Commands;
using Wms.Application.ReceivingOrders;
using Wms.Application.LicensePlateNumbers;
using Wms.Common;
using Wms.Contracts.Mobile.V1;
using Wms.Data;
using Wms.Domain;
using Wms.Domain.Enums;
using Wms.Integration.OneS;

namespace Wms.WebApi.Mobile;

internal static class MobileReceivingOrderEndpoints
{
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
        group.MapPost("/{orderId:guid}/complete-receiving", CompleteReceivingAsync)
            .WithMobileResponses<MobileReceivingOrderCommandResponse>();
        group.MapPost("/{orderId:guid}/lpns/open", OpenLicensePlateNumberAsync)
            .WithMobileResponses<MobileReceivingOrderCommandResponse>();
        group.MapPost("/{orderId:guid}/lpns/{lpnId:guid}/cancel", CancelLicensePlateNumberAsync)
            .WithMobileResponses<MobileReceivingOrderCommandResponse>();
        group.MapPost("/{orderId:guid}/lines/resolve-sku", ResolveSkuAsync)
            .WithMobileResponses<IReadOnlyList<MobileReceivingOrderLineCandidateResponse>>();
        group.MapPost("/{orderId:guid}/lpns/{lpnId:guid}/items/{skuId:guid}/quantity", SetLicensePlateNumberItemQuantityAsync)
            .WithMobileResponses<MobileReceivingOrderCommandResponse>();
        group.MapPost("/{orderId:guid}/lpns/{lpnId:guid}/items/{skuId:guid}/remove", RemoveLicensePlateNumberItemAsync)
            .WithMobileResponses<MobileReceivingOrderCommandResponse>();
        group.MapPost("/{orderId:guid}/lpns/{lpnId:guid}/close", CloseLicensePlateNumberAsync)
            .WithMobileResponses<MobileReceivingOrderCommandResponse>();
        group.MapPost("/{orderId:guid}/lpns/{lpnId:guid}/reopen", ReopenLicensePlateNumberAsync)
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
            queue.Select(x => MapSummary(x)).ToList()));
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

        var synchronizationResult = await CheckForMobileAsync(
            result.Value!.Order.Id,
            result.Value.Order.Status,
            synchronizationService,
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
            ? TypedResults.Ok(MapDetails(
                currentResult.Value!,
                synchronizationResult.Value.Assessment,
                requiresManager: synchronizationResult.Value.RequiresManager))
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

        var synchronizationResult = await CheckForMobileAsync(
            result.Value.Order.Id,
            result.Value.Order.Status,
            synchronizationService,
            ct);
        if (!synchronizationResult.IsSuccess)
        {
            return TypedResults.Ok(MapDetails(
                result.Value!,
                verificationError: synchronizationResult.Error?.Message
                    ?? "Не удалось сверить приходный ордер с 1С."));
        }

        var currentResult = await queryService.GetDetailsAsync(orderId, userId, ct);
        return currentResult.IsSuccess
            ? TypedResults.Ok(MapDetails(
                currentResult.Value!,
                synchronizationResult.Value.Assessment,
                requiresManager: synchronizationResult.Value.RequiresManager))
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

    private static async Task<OperationResult<MobileReceivingSynchronization>> CheckForMobileAsync(
        Guid orderId,
        ReceivingOrderStatus status,
        ReceivingOrderSynchronizationService synchronizationService,
        CancellationToken ct)
    {
        if (status != ReceivingOrderStatus.InReceiving)
        {
            var synchronization = await synchronizationService.CheckAsync(orderId, ct);
            return synchronization.IsSuccess
                ? new MobileReceivingSynchronization(synchronization.Value!, false)
                : synchronization.Error!;
        }

        var completion = await synchronizationService.CheckCompletionAsync(orderId, ct);
        return completion.IsSuccess
            ? new MobileReceivingSynchronization(
                completion.Value!.Assessment,
                completion.Value.RequiresManager)
            : completion.Error!;
    }

    private static async Task<IResult> CompleteReceivingAsync(
        Guid orderId,
        MobileCompleteReceivingOrderRequest request,
        ClaimsPrincipal principal,
        ReceivingOrderCommandService commandService,
        MobileReceivingOrderQueryService queryService,
        CancellationToken ct)
    {
        var userId = GetUserId(principal);
        if (userId is null)
            return TypedResults.Unauthorized();

        var result = await commandService.CompleteReceivingAsync(
            new CompleteReceivingCommand(orderId, null, request.ExpectedFingerprint, null),
            new CommandContext(request.ClientRequestId, userId),
            canApproveQuantityDifference: false,
            ct: ct);
        return await CommandResultAsync(result, orderId, userId, queryService, ct);
    }

    private static async Task<IResult> OpenLicensePlateNumberAsync(
        Guid orderId,
        MobileOpenReceivingLicensePlateNumberRequest request,
        ClaimsPrincipal principal,
        LicensePlateNumberService service,
        MobileReceivingOrderQueryService queryService,
        CancellationToken ct)
    {
        var userId = GetUserId(principal);
        if (userId is null)
            return TypedResults.Unauthorized();

        var result = await service.OpenForReceivingAsync(
            new OpenReceivingLicensePlateNumberCommand(orderId, request.Code),
            new CommandContext(request.ClientRequestId, userId),
            ct);
        return await LicensePlateNumberCommandResultAsync(
            result, orderId, userId, queryService, ct);
    }

    private static async Task<IResult> CancelLicensePlateNumberAsync(
        Guid orderId,
        Guid lpnId,
        MobileReceivingOrderCommandRequest request,
        ClaimsPrincipal principal,
        LicensePlateNumberService service,
        MobileReceivingOrderQueryService queryService,
        CancellationToken ct)
    {
        var userId = GetUserId(principal);
        if (userId is null)
            return TypedResults.Unauthorized();

        var result = await service.CancelEmptyReceivingAsync(
            new CancelReceivingLicensePlateNumberCommand(orderId, lpnId),
            new CommandContext(request.ClientRequestId, userId),
            ct);
        return await LicensePlateNumberCommandResultAsync(
            result, orderId, userId, queryService, ct);
    }

    private static async Task<IResult> LicensePlateNumberCommandResultAsync(
        OperationResult<Guid> result,
        Guid orderId,
        string userId,
        MobileReceivingOrderQueryService queryService,
        CancellationToken ct)
    {
        if (!result.IsSuccess)
            return MobileEndpointResults.CommandProblem(result.Error!);

        var details = await queryService.GetCommandResultDetailsAsync(orderId, userId, ct);
        return details.IsSuccess
            ? TypedResults.Ok(new MobileReceivingOrderCommandResponse(
                MapDetails(details.Value!),
                SelectedLicensePlateNumberId: result.Value))
            : MobileEndpointResults.CommandProblem(details.Error!);
    }

    private static async Task<IResult> SetLicensePlateNumberItemQuantityAsync(
        Guid orderId,
        Guid lpnId,
        Guid skuId,
        MobileSetReceivingLicensePlateNumberItemQuantityRequest request,
        ClaimsPrincipal principal,
        LicensePlateNumberService service,
        MobileReceivingOrderQueryService queryService,
        CancellationToken ct)
    {
        var userId = GetUserId(principal);
        if (userId is null)
            return TypedResults.Unauthorized();
        var result = await service.SetReceivingItemQuantityAsync(
            new(orderId, lpnId, skuId, request.Quantity),
            new(request.ClientRequestId, userId),
            CanManageLpn(principal),
            ct);
        return await LicensePlateNumberCommandResultAsync(result, orderId, userId, queryService, ct);
    }

    private static async Task<IResult> RemoveLicensePlateNumberItemAsync(
        Guid orderId,
        Guid lpnId,
        Guid skuId,
        MobileReceivingOrderCommandRequest request,
        ClaimsPrincipal principal,
        LicensePlateNumberService service,
        MobileReceivingOrderQueryService queryService,
        CancellationToken ct)
    {
        var userId = GetUserId(principal);
        if (userId is null)
            return TypedResults.Unauthorized();
        var result = await service.RemoveReceivingItemAsync(
            new(orderId, lpnId, skuId),
            new(request.ClientRequestId, userId),
            CanManageLpn(principal),
            ct);
        return await LicensePlateNumberCommandResultAsync(result, orderId, userId, queryService, ct);
    }

    private static async Task<IResult> CloseLicensePlateNumberAsync(
        Guid orderId,
        Guid lpnId,
        MobileReceivingOrderCommandRequest request,
        ClaimsPrincipal principal,
        LicensePlateNumberService service,
        MobileReceivingOrderQueryService queryService,
        CancellationToken ct)
    {
        var userId = GetUserId(principal);
        if (userId is null)
            return TypedResults.Unauthorized();
        var result = await service.CloseReceivingAsync(
            new(orderId, lpnId), new(request.ClientRequestId, userId), CanManageLpn(principal), ct);
        return await LicensePlateNumberCommandResultAsync(result, orderId, userId, queryService, ct);
    }

    private static async Task<IResult> ReopenLicensePlateNumberAsync(
        Guid orderId,
        Guid lpnId,
        MobileReceivingOrderCommandRequest request,
        ClaimsPrincipal principal,
        LicensePlateNumberService service,
        MobileReceivingOrderQueryService queryService,
        CancellationToken ct)
    {
        var userId = GetUserId(principal);
        if (userId is null)
            return TypedResults.Unauthorized();
        var result = await service.ReopenReceivingAsync(
            new(orderId, lpnId), new(request.ClientRequestId, userId), CanManageLpn(principal), ct);
        return await LicensePlateNumberCommandResultAsync(result, orderId, userId, queryService, ct);
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

    private static MobileReceivingOrderDetailsResponse MapDetails(
        MobileReceivingOrderDetails details,
        OrderSynchronizationAssessment? assessment = null,
        string? verificationError = null,
        bool requiresManager = false) => new(
        MapSummary(details.Order, assessment, verificationError, requiresManager),
        details.Lines.Select(MapLine).ToList(),
        details.LicensePlateNumbers.Select(MapLicensePlateNumber).ToList());

    private static MobileReceivingOrderLicensePlateNumberResponse MapLicensePlateNumber(
        MobileReceivingOrderLicensePlateNumber lpn) => new(
        lpn.Id,
        lpn.Code,
        lpn.Status switch
        {
            LicensePlateNumberStatus.Available => MobileLicensePlateNumberStatus.Available,
            LicensePlateNumberStatus.Open => MobileLicensePlateNumberStatus.Open,
            LicensePlateNumberStatus.Closed => MobileLicensePlateNumberStatus.Closed,
            LicensePlateNumberStatus.Cancelled => MobileLicensePlateNumberStatus.Cancelled,
            _ => throw new InvalidOperationException($"Неизвестный статус LPN: {lpn.Status}.")
        },
        lpn.Status.GetDisplayName(),
        lpn.ResponsibleUserName,
        lpn.CreatedByName,
        lpn.CreatedAtUtc,
        lpn.ClosedByName,
        lpn.ClosedAtUtc,
        lpn.ReopenedByName,
        lpn.ReopenedAtUtc,
        lpn.CancelledAtUtc,
        lpn.IsOwnedByCurrentUser,
        lpn.CanEdit,
        lpn.CanCancel,
        lpn.CanClose,
        lpn.CanReopen,
        lpn.Items.Select(x => new MobileReceivingOrderLicensePlateNumberItemResponse(
            x.StockKeepingUnitId,
            x.SkuCode,
            x.SkuName,
            x.UnitOfMeasure,
            x.Quantity,
            x.CreatedByName,
            x.CreatedAtUtc,
            x.UpdatedByName,
            x.UpdatedAtUtc)).ToList());

    private static MobileReceivingOrderSummaryResponse MapSummary(
        MobileReceivingOrderSummary order,
        OrderSynchronizationAssessment? assessment = null,
        string? verificationError = null,
        bool requiresManager = false) => new(
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
        MapSynchronization(order.SynchronizationLevel, assessment, verificationError, requiresManager),
        order.Comment,
        order.ReceivingLocation is null ? null : MapLocation(order.ReceivingLocation),
        new MobileReceivingOrderProgressResponse(
            order.TotalLineCount,
            order.ConfirmedLineCount,
            order.PositiveLineCount),
        order.StartedAtUtc,
        order.CompletedAtUtc,
        order.IsParticipant,
        // CanJoin comes from the persisted source assessment. For an in-progress
        // order, assessment describes completion and may include unchecked facts.
        order.CanJoin
            && string.IsNullOrWhiteSpace(verificationError),
        verificationError ?? order.JoinBlockedReason);

    private static MobileOrderSynchronizationResponse MapSynchronization(
        OrderSynchronizationLevel persistedLevel,
        OrderSynchronizationAssessment? assessment,
        string? verificationError = null,
        bool requiresManager = false)
    {
        var commentDifference = assessment?.Differences
            .LastOrDefault(x => x.FieldCode == "comment");
        return new MobileOrderSynchronizationResponse(
            MapSynchronizationLevel(assessment?.Level ?? persistedLevel),
            assessment is not null,
            assessment?.Differences.Select(x => x.FieldName).Distinct().ToList() ?? [],
            commentDifference is not null,
            commentDifference?.OneCValue,
            verificationError,
            assessment?.Fingerprint,
            requiresManager,
            MapSynchronizationLevel(persistedLevel));
    }

    private sealed record MobileReceivingSynchronization(
        OrderSynchronizationAssessment Assessment,
        bool RequiresManager);

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
        line.Comment);

    private static MobileReceivingOrderLineCandidateResponse MapCandidate(
        MobileReceivingOrderLineCandidate candidate) => new(
        candidate.LineNumber,
        candidate.StockKeepingUnitId,
        candidate.SkuCode,
        candidate.SkuName,
        candidate.UnitOfMeasure,
        candidate.PlanQuantity,
        candidate.FactQuantity,
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

    private static string? GetUserId(ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.NameIdentifier);

    private static bool CanManageLpn(ClaimsPrincipal principal) =>
        principal.IsInRole(ApplicationRoles.Manager)
        || principal.IsInRole(ApplicationRoles.Administrator);

}
