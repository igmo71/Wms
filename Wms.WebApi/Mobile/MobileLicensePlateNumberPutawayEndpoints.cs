using System.Security.Claims;
using Wms.Application.Commands;
using Wms.Application.LicensePlateNumbers;
using Wms.Application.StorageLocations;
using Wms.Contracts.Mobile.V1;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.WebApi.Mobile;

internal static class MobileLicensePlateNumberPutawayEndpoints
{
    public static IEndpointRouteBuilder MapMobileLicensePlateNumberPutawayEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(MobileApiRoutes.LicensePlateNumberPutaway)
            .WithTags("Mobile LPN Putaway")
            .RequireAuthorization(MobileAuthorization.WarehouseOperatorPolicy);

        group.MapPost("/resolve", ResolveAsync)
            .WithMobileResponses<MobileLicensePlateNumberPutawayDetailsResponse>();
        group.MapGet("/{lpnId:guid}", GetAsync)
            .WithMobileResponses<MobileLicensePlateNumberPutawayDetailsResponse>();
        group.MapPost("/{lpnId:guid}/resolve-sku", ResolveSkuAsync)
            .WithMobileResponses<MobileLicensePlateNumberPutawaySelectionResponse>();
        group.MapGet("/{lpnId:guid}/items/{lpnItemId:guid}/selection", GetSelectionAsync)
            .WithMobileResponses<MobileLicensePlateNumberPutawaySelectionResponse>();
        group.MapPost("/{lpnId:guid}/putaway", PutawayAsync)
            .WithMobileResponses<MobileLicensePlateNumberPutawayCommandResponse>();
        return endpoints;
    }

    private static async Task<IResult> ResolveAsync(
        MobileResolveLicensePlateNumberPutawayRequest request,
        LicensePlateNumberPutawayService service,
        CancellationToken ct) =>
        MapDetails(await service.GetByCodeAsync(request.Code, ct));

    private static async Task<IResult> GetAsync(
        Guid lpnId,
        LicensePlateNumberPutawayService service,
        CancellationToken ct) =>
        MapDetails(await service.GetAsync(lpnId, ct));

    private static async Task<IResult> ResolveSkuAsync(
        Guid lpnId,
        MobileResolveLicensePlateNumberPutawaySkuRequest request,
        LicensePlateNumberPutawayService service,
        CancellationToken ct)
    {
        var result = await service.ResolveSkuAsync(lpnId, request.Barcode, ct);
        return result.IsSuccess
            ? TypedResults.Ok(MapSelection(result.Value!))
            : MobileEndpointResults.CommandProblem(result.Error!);
    }

    private static async Task<IResult> GetSelectionAsync(
        Guid lpnId,
        Guid lpnItemId,
        LicensePlateNumberPutawayService service,
        CancellationToken ct)
    {
        var result = await service.GetSelectionAsync(lpnId, lpnItemId, ct);
        return result.IsSuccess
            ? TypedResults.Ok(MapSelection(result.Value!))
            : MobileEndpointResults.CommandProblem(result.Error!);
    }

    private static async Task<IResult> PutawayAsync(
        Guid lpnId,
        MobilePutawayLicensePlateNumberRequest request,
        ClaimsPrincipal principal,
        LicensePlateNumberPutawayService service,
        StorageLocationQueryService storageLocationQueryService,
        CancellationToken ct)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
            return TypedResults.Unauthorized();
        var lpn = await service.GetAsync(lpnId, ct);
        if (!lpn.IsSuccess)
            return MobileEndpointResults.CommandProblem(lpn.Error!);
        var location = await storageLocationQueryService.ResolveBarcodeAsync(
            request.DestinationStorageLocationBarcode, lpn.Value!.WarehouseId, ZoneType.Storage, ct);
        if (!location.IsSuccess)
            return MobileEndpointResults.CommandProblem(location.Error!);
        var destinationStorageLocationId = location.Value!.Id;

        var result = await service.PutawayAsync(
            new PutawayLicensePlateNumberCommand(
                lpnId,
                request.LicensePlateNumberItemId,
                destinationStorageLocationId,
                request.Quantity),
            new CommandContext(request.ClientRequestId, userId),
            ct);
        if (!result.IsSuccess)
            return MobileEndpointResults.CommandProblem(result.Error!);

        var details = await service.GetAsync(lpnId, ct);
        return details.IsSuccess
            ? TypedResults.Ok(new MobileLicensePlateNumberPutawayCommandResponse(
                Map(details.Value!), result.Value))
            : MobileEndpointResults.CommandProblem(details.Error!);
    }

    private static IResult MapDetails(
        Wms.Common.OperationResult<LicensePlateNumberPutawayDetails> result) =>
        result.IsSuccess
            ? TypedResults.Ok(Map(result.Value!))
            : MobileEndpointResults.CommandProblem(result.Error!);

    private static MobileLicensePlateNumberPutawayDetailsResponse Map(
        LicensePlateNumberPutawayDetails details) => new(
            details.Id,
            details.Code,
            details.OrderNumber,
            details.WarehouseId,
            details.CanPutaway,
            details.BlockedReason,
            details.IsCompleted,
            details.Items.Select(MapItem).ToList(),
            details.History.Select(x => new MobileLicensePlateNumberPutawayHistoryResponse(
                x.Id, x.LicensePlateNumberItemId, x.StockKeepingUnitId,
                x.SkuName, x.UnitOfMeasure, x.Quantity, x.DestinationAddress, x.DestinationName,
                x.CreatedBy, x.CreatedAtUtc, x.InventoryMovementId, x.CreatedByName)).ToList());

    private static MobileLicensePlateNumberPutawayItemResponse MapItem(
        LicensePlateNumberPutawayItem item) => new(
            item.Id,
            item.StockKeepingUnitId,
            item.SkuCode,
            item.SkuName,
            item.UnitOfMeasure,
            item.AcceptedQuantity,
            item.PlacedQuantity,
            item.RemainingQuantity);

    private static MobileLicensePlateNumberPutawaySelectionResponse MapSelection(
        LicensePlateNumberPutawaySelection selection) => new(
            MapItem(selection.Item),
            selection.Recommendations.Select(x => new MobileLicensePlateNumberPutawayRecommendationResponse(
                x.StorageLocationId,
                x.Address,
                x.Name,
                (MobileLicensePlateNumberPutawayRecommendationCategory)x.Category,
                x.CurrentSkuQuantity)).ToList());
}
