using Wms.Application.SkuBarcodes;
using Wms.Application.StorageLocations;
using Wms.Common;
using Wms.Contracts.Mobile.V1;
using Wms.Domain.Enums;

namespace Wms.WebApi.Mobile;

internal static class MobileBarcodeEndpoints
{
    public static IEndpointRouteBuilder MapMobileBarcodeEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(MobileApiRoutes.Base + "/barcodes")
            .WithTags("Mobile Barcodes")
            .RequireAuthorization(MobileAuthorization.WarehouseOperatorPolicy);

        group.MapPost("/storage-location/resolve", ResolveStorageLocationAsync)
            .Produces<MobileStorageLocationResponse>()
            .Produces<MobileProblemResponse>(StatusCodes.Status400BadRequest)
            .Produces<MobileProblemResponse>(StatusCodes.Status404NotFound)
            .Produces<MobileProblemResponse>(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/sku/resolve", ResolveSkuAsync)
            .Produces<MobileSkuResponse>()
            .Produces<MobileProblemResponse>(StatusCodes.Status400BadRequest)
            .Produces<MobileProblemResponse>(StatusCodes.Status404NotFound)
            .Produces<MobileProblemResponse>(StatusCodes.Status409Conflict)
            .Produces<MobileProblemResponse>(StatusCodes.Status422UnprocessableEntity);

        return endpoints;
    }

    private static async Task<IResult> ResolveStorageLocationAsync(
        MobileResolveStorageLocationRequest request,
        StorageLocationQueryService queryService,
        CancellationToken ct)
    {
        if (!TryMapZoneType(request.Context, out var expectedZoneType))
        {
            return Problem(
                StatusCodes.Status400BadRequest,
                "invalid_storage_location_context",
                "Указан неизвестный контекст ячейки.");
        }

        var result = await queryService.ResolveBarcodeAsync(
            request.Barcode,
            request.ExpectedWarehouseId,
            expectedZoneType,
            ct);

        if (!result.IsSuccess)
        {
            return ResolutionProblem("storage_location", result.Error!);
        }

        var location = result.Value!;
        return TypedResults.Ok(new MobileStorageLocationResponse(
            location.Id,
            location.Name,
            $"{location.Zone!.Code}-{location.Code}",
            location.WarehouseId,
            location.Warehouse!.Name ?? string.Empty,
            location.ZoneId,
            location.Zone.Name,
            MapContext(location.Zone.Type)));
    }

    private static async Task<IResult> ResolveSkuAsync(
        MobileResolveSkuRequest request,
        SkuBarcodeService barcodeService,
        CancellationToken ct)
    {
        var result = await barcodeService.ResolveAsync(request.Barcode, ct);
        if (!result.IsSuccess)
        {
            return ResolutionProblem("sku", result.Error!);
        }

        var sku = result.Value!;
        return TypedResults.Ok(new MobileSkuResponse(
            sku.Id,
            sku.Code ?? string.Empty,
            sku.Name ?? string.Empty,
            sku.BaseUnitOfMeasure?.Name));
    }

    private static IResult ResolutionProblem(string subject, OperationError error)
    {
        var (statusCode, suffix) = error.Type switch
        {
            OperationErrorType.NotFound => (StatusCodes.Status404NotFound, "not_found"),
            OperationErrorType.Conflict => (StatusCodes.Status409Conflict, "ambiguous"),
            OperationErrorType.Invalid => (StatusCodes.Status422UnprocessableEntity, "not_allowed"),
            _ => (StatusCodes.Status400BadRequest, "resolution_failed")
        };

        return Problem(statusCode, $"{subject}_{suffix}", error.Message);
    }

    private static bool TryMapZoneType(
        MobileStorageLocationContext context,
        out ZoneType? zoneType)
    {
        zoneType = context switch
        {
            MobileStorageLocationContext.AnyOperational => null,
            MobileStorageLocationContext.Storage => ZoneType.Storage,
            MobileStorageLocationContext.Transit => ZoneType.Transit,
            MobileStorageLocationContext.Receiving => ZoneType.Receiving,
            MobileStorageLocationContext.Shipping => ZoneType.Shipping,
            _ => null
        };

        return context == MobileStorageLocationContext.AnyOperational || zoneType is not null;
    }

    private static MobileStorageLocationContext MapContext(ZoneType type) => type switch
    {
        ZoneType.Storage => MobileStorageLocationContext.Storage,
        ZoneType.Transit => MobileStorageLocationContext.Transit,
        ZoneType.Receiving => MobileStorageLocationContext.Receiving,
        ZoneType.Shipping => MobileStorageLocationContext.Shipping,
        _ => MobileStorageLocationContext.AnyOperational
    };

    private static IResult Problem(int statusCode, string code, string message) =>
        Results.Json(new MobileProblemResponse(code, message), statusCode: statusCode);
}
