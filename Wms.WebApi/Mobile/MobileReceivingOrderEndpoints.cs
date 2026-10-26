using System.Security.Claims;
using Wms.Application.Receiving;
using Wms.Application.Users;
using Wms.Common;
using Wms.Contracts.Mobile.V1;
using Wms.Data;
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
            .RequireAuthorization(MobileAuthorization.ReceivingWorkerPolicy);

        group.MapPost("/sources/resolve", ResolveDocumentAsync)
            .WithMobileResponses<MobileReceivingDocumentResolutionResponse>();
        group.MapPost("", CreateOrderAsync)
            .WithMobileResponses<MobileReceivingCommandIdResponse>();
        group.MapGet("", GetOrdersAsync)
            .WithMobileResponses<IReadOnlyList<MobileReceivingOrderQueueResponse>>();
        group.MapGet("/{orderId:guid}", GetOrderAsync)
            .WithMobileResponses<MobileReceivingWorkOrderResponse>();
        group.MapGet("/{orderId:guid}/sources", GetLinkedSourcesAsync)
            .WithMobileResponses<IReadOnlyList<MobileReceivingLinkedSourceResponse>>();
        group.MapPost("/{orderId:guid}/sources", AttachSourceAsync)
            .WithMobileResponses<MobileReceivingCommandIdResponse>();
        group.MapPost("/{orderId:guid}/sources/{sourceRefKey:guid}/detach", DetachSourceAsync)
            .WithMobileResponses<MobileReceivingCommandIdResponse>();
        group.MapGet("/{orderId:guid}/skus", SearchSkuAsync)
            .WithMobileResponses<IReadOnlyList<MobileReceivingSkuCandidateResponse>>();
        group.MapPost("/{orderId:guid}/skus/resolve-barcode", ResolveSkuAsync)
            .WithMobileResponses<MobileReceivingSkuCandidateResponse>();
        group.MapPost("/{orderId:guid}/skus/{skuId:guid}", AddSkuAsync)
            .WithMobileResponses<MobileReceivingCommandIdResponse>();
        group.MapPost("/{orderId:guid}/skus/{skuId:guid}/confirm-zero", ConfirmZeroAsync)
            .WithMobileResponses<MobileReceivingCommandIdResponse>();
        group.MapPost("/{orderId:guid}/join", JoinAsync)
            .WithMobileResponses<MobileReceivingCommandIdResponse>();
        group.MapPost("/{orderId:guid}/complete", CompleteOrderAsync)
            .WithMobileResponses<MobileReceivingCommandIdResponse>();
        group.MapPost("/{orderId:guid}/lpns", OpenLpnAsync)
            .WithMobileResponses<MobileReceivingCommandIdResponse>();
        group.MapPut("/{orderId:guid}/lpns/{lpnId:guid}", EditLpnAsync)
            .WithMobileResponses<MobileReceivingCommandIdResponse>();
        group.MapPost("/{orderId:guid}/lpns/{lpnId:guid}/clear", ClearLpnItemAsync)
            .WithMobileResponses<MobileReceivingCommandIdResponse>();
        group.MapPost("/{orderId:guid}/lpns/{lpnId:guid}/cancel", CancelLpnAsync)
            .WithMobileResponses<MobileReceivingCommandIdResponse>();
        group.MapPost("/{orderId:guid}/lpns/{lpnId:guid}/close", CloseLpnAsync)
            .WithMobileResponses<MobileReceivingCloseResultResponse>();
        return endpoints;
    }

    private static async Task<IResult> ResolveDocumentAsync(
        MobileReceivingResolveDocumentRequest request, Guid warehouseId,
        ClaimsPrincipal principal, SynchronizeReceivingSource synchronize,
        GetReceivingSources sources, IReceivingWorkQueryStore orders,
        ApplicationUserQueryService userNames,
        CancellationToken ct)
    {
        if (warehouseId == Guid.Empty)
            return MobileEndpointResults.CommandProblem(OperationError.Invalid("Выберите склад."));
        var decoded = OneSDocumentBarcodeCodec.Decode(request.Barcode);
        if (!decoded.IsSuccess)
            return MobileEndpointResults.CommandProblem(decoded.Error!);
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
            return TypedResults.Unauthorized();

        // A failed refresh leaves the last successful source version available.
        await synchronize.ExecuteAsync(decoded.Value, ct);
        var linkedOrderId = await orders.FindLinkedOrderIdBySourceAsync(decoded.Value, ct);
        var linked = linkedOrderId is Guid id
            ? await orders.GetMobileOrderAsync(id, userId, ct) : null;
        if (linked is not null)
        {
            if (linked.WarehouseId != warehouseId)
                return MobileEndpointResults.CommandProblem(
                    OperationError.Conflict("Документ относится к другому складу."));
            var names = await userNames.GetDisplayNamesAsync(
                linked.Lpns.Select(x => x.ResponsibleUserId), ct);
            return TypedResults.Ok(new MobileReceivingDocumentResolutionResponse(
                null, MapOrder(linked, principal, names)));
        }
        var source = (await sources.GetByRefKeysAsync([decoded.Value], ct))
            .SingleOrDefault(x => x.RefKey == decoded.Value);
        if (source is null)
            return MobileEndpointResults.CommandProblem(
                OperationError.NotFound("Приходный документ не найден."));
        return source.WarehouseId == warehouseId
            ? TypedResults.Ok(new MobileReceivingDocumentResolutionResponse(MapSource(source), null))
            : MobileEndpointResults.CommandProblem(
                OperationError.Conflict("Документ относится к другому складу."));
    }

    private static async Task<IResult> CreateOrderAsync(
        MobileReceivingCreateOrderRequest request,
        ClaimsPrincipal principal, CreateReceivingOrder create, CancellationToken ct)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
            return TypedResults.Unauthorized();
        return CommandId(await create.ExecuteAsync(new CreateReceivingOrderRequest(
            request.ClientRequestId, request.SourceRefKeys, userId), ct));
    }

    private static async Task<IResult> GetOrdersAsync(
        Guid warehouseId, ClaimsPrincipal principal,
        IReceivingWorkQueryStore orders,
        CancellationToken ct)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
            return TypedResults.Unauthorized();
        if (warehouseId == Guid.Empty)
            return MobileEndpointResults.CommandProblem(OperationError.Invalid("Выберите склад."));
        var results = await orders.ListQueueAsync(warehouseId, ct);
        return TypedResults.Ok<IReadOnlyList<MobileReceivingOrderQueueResponse>>(
            results.Select(x => new MobileReceivingOrderQueueResponse(
                x.Id, x.Number, x.CreatedAtUtc, x.WarehouseId,
                (MobileReceivingOrderStatus)x.Status)).ToArray());
    }

    private static async Task<IResult> GetOrderAsync(
        Guid orderId, ClaimsPrincipal principal,
        IReceivingWorkQueryStore orders, ApplicationUserQueryService userNames,
        CancellationToken ct)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
            return TypedResults.Unauthorized();
        var order = await orders.GetMobileOrderAsync(orderId, userId, ct);
        if (order is null)
            return MobileEndpointResults.CommandProblem(OperationError.NotFound("Приходный ордер не найден."));
        var names = await userNames.GetDisplayNamesAsync(
            order.Lpns.Select(x => x.ResponsibleUserId), ct);
        return TypedResults.Ok(MapOrder(order, principal, names));
    }

    private static async Task<IResult> GetLinkedSourcesAsync(
        Guid orderId, ClaimsPrincipal principal, IReceivingWorkQueryStore orders,
        GetReceivingSources sources, CancellationToken ct)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
            return TypedResults.Unauthorized();
        var sourceRefKeys = await orders.GetOrderSourceRefKeysAsync(orderId, ct);
        if (sourceRefKeys is null)
            return MobileEndpointResults.CommandProblem(
                OperationError.NotFound("Приходный ордер не найден."));
        var linked = await sources.GetLinkedSummariesAsync(sourceRefKeys, ct);
        return TypedResults.Ok<IReadOnlyList<MobileReceivingLinkedSourceResponse>>(
            linked.Select(x => new MobileReceivingLinkedSourceResponse(
                x.RefKey, x.Number, x.Date, x.Sender, x.SenderType,
                x.WarehouseOperation, x.BusinessOperation,
                x.BasisDocument, x.BasisDocumentType)
            {
                SenderName = x.SenderName,
                LastAttemptError = x.LastAttemptError
            }).ToArray());
    }

    private static async Task<IResult> AttachSourceAsync(
        Guid orderId, MobileReceivingAttachSourceRequest request,
        ClaimsPrincipal principal, ChangeReceivingOrderSources change, CancellationToken ct)
    {
        var decoded = OneSDocumentBarcodeCodec.Decode(request.Barcode);
        if (!decoded.IsSuccess)
            return MobileEndpointResults.CommandProblem(decoded.Error!);
        return await ExecuteForUserAsync(principal, userId => change.ExecuteAsync(
            new ChangeReceivingOrderSourcesRequest(request.ClientRequestId, orderId,
                decoded.Value, userId, ReceivingSourceChangeKind.Attach,
                ReceivingSourceChangeChannel.Mobile), ct));
    }

    private static async Task<IResult> DetachSourceAsync(
        Guid orderId, Guid sourceRefKey, MobileReceivingCommandRequest request,
        ClaimsPrincipal principal, ChangeReceivingOrderSources change, CancellationToken ct) =>
        await ExecuteForUserAsync(principal, userId => change.ExecuteAsync(
            new ChangeReceivingOrderSourcesRequest(request.ClientRequestId, orderId,
                sourceRefKey, userId, ReceivingSourceChangeKind.Detach,
                ReceivingSourceChangeChannel.Mobile), ct));

    private static async Task<IResult> SearchSkuAsync(
        Guid orderId, string? search, ReceivingSkuSearchScope scope,
        int? take, SearchReceivingSku sku, CancellationToken ct)
    {
        var result = await sku.SearchAsync(orderId, search, scope, take ?? 30, ct);
        return result.IsSuccess
            ? TypedResults.Ok<IReadOnlyList<MobileReceivingSkuCandidateResponse>>(
                result.Value!.Select(MapSku).ToArray())
            : MobileEndpointResults.CommandProblem(result.Error!);
    }

    private static async Task<IResult> ResolveSkuAsync(
        Guid orderId, MobileReceivingBarcodeRequest request,
        SearchReceivingSku sku, CancellationToken ct)
    {
        var result = await sku.ResolveBarcodeAsync(orderId, request.Barcode, ct);
        return result.IsSuccess ? TypedResults.Ok(MapSku(result.Value!))
            : MobileEndpointResults.CommandProblem(result.Error!);
    }

    private static async Task<IResult> AddSkuAsync(
        Guid orderId, Guid skuId, MobileReceivingCommandRequest request,
        ClaimsPrincipal principal, AddReceivingOrderSku add, CancellationToken ct) =>
        await ExecuteForUserAsync(principal, userId => add.ExecuteAsync(
            new AddReceivingOrderSkuRequest(request.ClientRequestId, orderId, skuId, userId), ct));

    private static async Task<IResult> ConfirmZeroAsync(
        Guid orderId, Guid skuId, MobileReceivingCommandRequest request,
        ClaimsPrincipal principal, ConfirmZeroReceived confirm, CancellationToken ct) =>
        await ExecuteForUserAsync(principal, userId => confirm.ExecuteAsync(
            new ConfirmZeroReceivedRequest(request.ClientRequestId, orderId, skuId, userId), ct));

    private static async Task<IResult> JoinAsync(
        Guid orderId, MobileReceivingJoinCycleRequest request,
        ClaimsPrincipal principal, JoinReceivingCycle join, CancellationToken ct) =>
        await ExecuteForUserAsync(principal, userId => join.ExecuteAsync(
            new JoinReceivingCycleRequest(request.ClientRequestId, orderId,
                request.ReceivingLocationId, userId), ct));

    private static async Task<IResult> CompleteOrderAsync(
        Guid orderId, MobileReceivingCommandRequest request,
        ClaimsPrincipal principal, CompleteReceivingOrder complete, CancellationToken ct) =>
        await ExecuteForUserAsync(principal, userId => complete.ExecuteAsync(
            new CompleteReceivingOrderRequest(request.ClientRequestId, orderId, userId), ct));

    private static async Task<IResult> OpenLpnAsync(
        Guid orderId, MobileReceivingOpenLpnRequest request,
        ClaimsPrincipal principal, OpenReceivingLpn open, CancellationToken ct) =>
        await ExecuteForUserAsync(principal, userId => open.ExecuteAsync(
            new OpenReceivingLpnRequest(request.ClientRequestId, orderId,
                request.LpnCode, userId), ct));

    private static async Task<IResult> EditLpnAsync(
        Guid orderId, Guid lpnId, MobileReceivingEditLpnRequest request,
        ClaimsPrincipal principal, EditReceivingLpn edit, CancellationToken ct) =>
        await ExecuteForUserAsync(principal, userId => edit.ExecuteAsync(
            new EditReceivingLpnRequest(request.ClientRequestId, orderId,
                lpnId, request.SkuId, request.Quantity, request.ExpirationDate, userId), ct));

    private static async Task<IResult> ClearLpnItemAsync(
        Guid orderId, Guid lpnId, MobileReceivingClearLpnItemRequest request,
        ClaimsPrincipal principal, ClearReceivingLpnItem clear, CancellationToken ct) =>
        await ExecuteForUserAsync(principal, userId => clear.ExecuteAsync(
            new ClearReceivingLpnItemRequest(request.ClientRequestId, orderId,
                lpnId, request.ExpectedRevision, userId), ct));

    private static async Task<IResult> CancelLpnAsync(
        Guid orderId, Guid lpnId, MobileReceivingCommandRequest request,
        ClaimsPrincipal principal, CancelReceivingLpn cancel, CancellationToken ct) =>
        await ExecuteForUserAsync(principal, userId => cancel.ExecuteAsync(
            new CancelReceivingLpnRequest(request.ClientRequestId, orderId, lpnId, userId), ct));

    private static async Task<IResult> CloseLpnAsync(
        Guid orderId, Guid lpnId, MobileReceivingCloseLpnRequest request,
        ClaimsPrincipal principal, CloseReceivingLpn close, CancellationToken ct)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
            return TypedResults.Unauthorized();
        var result = await close.ExecuteAsync(new CloseReceivingLpnCommand(
            request.ClientRequestId, orderId, lpnId, userId,
            request.ConfirmExpiredDate, request.ManagerReason), ct);
        if (!result.IsSuccess)
            return MobileEndpointResults.CommandProblem(result.Error!);
        var value = result.Value!;
        return TypedResults.Ok(new MobileReceivingCloseResultResponse(
            value.LpnId, value.UsedPlan, value.TotalFact,
            value.SourceCheckSucceeded, value.SourceCheckError,
            value.LastSuccessfulCheckAtUtc, value.ManagerException,
            value.ExpiredDateConfirmed));
    }

    private static async Task<IResult> ExecuteForUserAsync(
        ClaimsPrincipal principal, Func<string, Task<OperationResult<Guid>>> execute)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return userId is null ? TypedResults.Unauthorized()
            : CommandId(await execute(userId));
    }

    private static IResult CommandId(OperationResult<Guid> result) =>
        result.IsSuccess ? TypedResults.Ok(new MobileReceivingCommandIdResponse(result.Value))
            : MobileEndpointResults.CommandProblem(result.Error!);

    private static MobileReceivingSourceResponse MapSource(ReceivingSourceView source) => new(
        source.RefKey, source.WarehouseId, source.Number,
        source.Posted, source.DeletionMark, source.VersionId,
        source.LastSuccessfulCheckAtUtc, source.LastAttemptAtUtc,
        source.LastAttemptError, source.HasUnknownSku,
        source.PlanBySku,
        source.Lines.Select(x => new MobileReceivingSourceLineResponse(
            x.LineNumber, x.SourceSkuRefKey, x.StockKeepingUnitId, x.RawQuantity,
            x.Quantity, x.Error)).ToArray())
    {
        WarehouseName = source.WarehouseName,
        SkuNames = source.SkuNames
    };

    private static MobileReceivingSkuCandidateResponse MapSku(ReceivingSkuCandidate sku) => new(
        sku.SkuId, sku.Name, sku.Code, sku.TracksExpirationDate,
        sku.IsInOrder, sku.CurrentPlanQuantity, sku.FactQuantity);

    private static MobileReceivingWorkOrderResponse MapOrder(
        ReceivingWorkOrderView order, ClaimsPrincipal principal,
        IReadOnlyDictionary<string, string> responsibleUserNames) => new(
        order.Id, order.Number, order.CreatedAtUtc,
        order.WarehouseId, (MobileReceivingOrderStatus)order.Status,
        order.ActiveCycleNumber, order.ReceivingLocationId,
        order.IsParticipant, order.Revision, order.SourceRefKeys,
        order.Items.Select(x => new MobileReceivingWorkItemResponse(
            x.SkuId, x.SkuCode, x.SkuName, x.TracksExpirationDate,
            x.UnitOfMeasure, x.PlanQuantity, x.FactQuantity)
        { IsCurrent = x.IsCurrent }).ToArray(),
        order.Lpns.Select(lpn => MapLpn(lpn) with
        {
            ResponsibleUserName = lpn.ResponsibleUserId is { } id
                && responsibleUserNames.TryGetValue(id, out var name)
                ? name : lpn.ResponsibleUserId is null
                    ? "Ответственный не указан" : "Имя не указано"
        }).ToArray())
    {
        WarehouseName = order.WarehouseName,
        ReceivingLocationAddress = order.ReceivingLocationAddress,
        MayCompleteOrder = order.Status == ReceivingOrderStatus.InReceiving
            && (order.IsParticipant || principal.IsInRole(ApplicationRoles.Manager))
    };

    internal static MobileReceivingWorkLpnResponse MapLpn(ReceivingWorkLpnView lpn) => new(
        lpn.Id, lpn.Code, lpn.OrderId, lpn.WarehouseId,
        (MobileLicensePlateNumberStatus)lpn.Status,
        lpn.ResponsibleUserId, lpn.OriginalReceivingLocationId, lpn.Revision,
        lpn.Items.Select(x => new MobileReceivingWorkLpnItemResponse(
            x.Id, x.SkuId, x.SkuCode, x.SkuName, x.UnitOfMeasure,
            x.Quantity, x.ExpirationDate, x.PlacedQuantity,
            x.Quantity - x.PlacedQuantity)).ToArray(),
        lpn.Putaways.Select(x => new MobileReceivingWorkPutawayResponse(
            x.Id, x.ItemId, x.DestinationStorageLocationId,
            x.Quantity, x.CreatedAtUtc, x.MovementId)
        {
            DestinationAddress = x.DestinationAddress
        }).ToArray());
}
