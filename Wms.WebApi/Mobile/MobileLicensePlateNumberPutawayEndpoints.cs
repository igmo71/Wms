using System.Security.Claims;
using Wms.Application.Receiving;
using Wms.Common;
using Wms.Contracts.Mobile.V1;
using Wms.Domain.Enums;

namespace Wms.WebApi.Mobile;

internal static class MobileLicensePlateNumberPutawayEndpoints
{
    public static IEndpointRouteBuilder MapMobileLicensePlateNumberPutawayEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(MobileApiRoutes.LicensePlateNumberPutaway)
            .WithTags("Mobile LPN Putaway")
            .RequireAuthorization(MobileAuthorization.ReceivingWorkerPolicy);
        group.MapGet("/resolve", ResolveAsync)
            .WithMobileResponses<MobileReceivingWorkLpnResponse>();
        group.MapGet("/{lpnId:guid}", GetAsync)
            .WithMobileResponses<MobileReceivingWorkLpnResponse>();
        group.MapGet("/{lpnId:guid}/items/{itemId:guid}/selection", GetSelectionAsync)
            .WithMobileResponses<MobileReceivingPutawaySelectionResponse>();
        group.MapPost("/{lpnId:guid}/resolve-sku", ResolveSkuAsync)
            .WithMobileResponses<MobileReceivingPutawaySelectionResponse>();
        group.MapPost("/{lpnId:guid}/putaways", PutawayAsync)
            .WithMobileResponses<MobileReceivingCommandIdResponse>();
        return endpoints;
    }

    private static async Task<IResult> ResolveAsync(
        string? code, IReceivingWorkQueryStore query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code))
            return MobileEndpointResults.CommandProblem(
                OperationError.Invalid("Отсканируйте LPN."));
        var lpn = await query.GetLpnAsync(null, code.Trim().ToUpperInvariant(), ct);
        return MapLpn(lpn);
    }

    private static async Task<IResult> GetAsync(
        Guid lpnId, IReceivingWorkQueryStore query, CancellationToken ct) =>
        MapLpn(await query.GetLpnAsync(lpnId, null, ct));

    private static async Task<IResult> GetSelectionAsync(
        Guid lpnId, Guid itemId, IReceivingWorkQueryStore query,
        CancellationToken ct)
    {
        var lpn = await query.GetLpnAsync(lpnId, null, ct);
        if (lpn is null)
            return MobileEndpointResults.CommandProblem(OperationError.NotFound("LPN не найден."));
        if (lpn.Status != LicensePlateNumberStatus.Closed)
            return MobileEndpointResults.CommandProblem(
                OperationError.Conflict("Размещать можно только закрытый LPN."));
        var item = MobileReceivingOrderEndpoints.MapLpn(lpn).Items
            .SingleOrDefault(x => x.Id == itemId && x.RemainingQuantity > 0);
        if (item is null)
            return MobileEndpointResults.CommandProblem(
                OperationError.NotFound("В LPN нет выбранной строки с остатком к размещению."));
        var recommendations = await query.GetPutawayRecommendationsAsync(
            lpn.WarehouseId, item.SkuId, ct);
        return TypedResults.Ok(new MobileReceivingPutawaySelectionResponse(
            item, recommendations.Select(x =>
                new MobileReceivingPutawayRecommendationResponse(
                    x.StorageLocationId, x.Address, x.Name,
                    x.CurrentSkuQuantity is > 0
                        ? MobileReceivingPutawayRecommendationKind.CurrentSkuStock
                        : MobileReceivingPutawayRecommendationKind.EmptyWithSkuBalanceHistory,
                    x.CurrentSkuQuantity)).ToArray()));
    }

    private static async Task<IResult> ResolveSkuAsync(
        Guid lpnId, MobileReceivingBarcodeRequest request,
        IReceivingWorkQueryStore query, SearchReceivingSku search,
        CancellationToken ct)
    {
        var lpn = await query.GetLpnAsync(lpnId, null, ct);
        if (lpn is null)
            return MobileEndpointResults.CommandProblem(OperationError.NotFound("LPN не найден."));
        var sku = await search.ResolveBarcodeAsync(lpn.OrderId, request.Barcode, ct);
        if (!sku.IsSuccess)
            return MobileEndpointResults.CommandProblem(sku.Error!);
        var item = lpn.Items.SingleOrDefault(x => x.SkuId == sku.Value!.SkuId
            && x.Quantity > x.PlacedQuantity);
        return item is null
            ? MobileEndpointResults.CommandProblem(OperationError.NotFound(
                "В LPN нет этого SKU с остатком к размещению."))
            : await GetSelectionAsync(lpnId, item.Id, query, ct);
    }

    private static async Task<IResult> PutawayAsync(
        Guid lpnId, MobileReceivingPutawayRequest request,
        ClaimsPrincipal principal, PutAwayReceivingLpn putaway,
        CancellationToken ct)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
            return TypedResults.Unauthorized();
        var result = await putaway.ExecuteAsync(new PutAwayReceivingLpnCommand(
            request.ClientRequestId, lpnId, request.LpnItemId,
            request.DestinationStorageLocationId, request.Quantity, userId), ct);
        return result.IsSuccess
            ? TypedResults.Ok(new MobileReceivingCommandIdResponse(result.Value))
            : MobileEndpointResults.CommandProblem(result.Error!);
    }

    private static IResult MapLpn(ReceivingWorkLpnView? lpn) =>
        lpn is null
            ? MobileEndpointResults.CommandProblem(OperationError.NotFound("LPN не найден."))
            : TypedResults.Ok(MobileReceivingOrderEndpoints.MapLpn(lpn));
}
