using System.Security.Claims;
using Wms.Application.Inventory;
using Wms.Contracts.Mobile.V1;

namespace Wms.WebApi.Mobile;

internal static class MobileStockAvailabilityEndpoints
{
    public static IEndpointRouteBuilder MapMobileStockAvailabilityEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(MobileApiRoutes.StockAvailability + "/{warehouseId:guid}/skus/{skuId:guid}",
                GetAsync)
            .WithTags("Mobile Stock Availability")
            .RequireAuthorization(MobileAuthorization.PickingWorkerPolicy)
            .WithMobileResponses<MobileStockAvailabilityResponse>();
        return endpoints;
    }

    private static async Task<IResult> GetAsync(Guid warehouseId, Guid skuId,
        ClaimsPrincipal principal, GetStockAvailability availability,
        CancellationToken ct)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return TypedResults.Unauthorized();
        var result = await availability.ExecuteAsync(warehouseId, skuId, userId, ct);
        if (!result.IsSuccess)
            return MobileEndpointResults.CommandProblem(result.Error!);
        return TypedResults.Ok(new MobileStockAvailabilityResponse(warehouseId, skuId,
            result.Value!.Where(x => x.AccountingQuantity != 0
                    || x.AvailableQuantity != 0)
                .OrderBy(x => x.LocationCode, StringComparer.Ordinal)
                .ThenBy(x => x.Group.ExpirationDate is null)
                .ThenBy(x => x.Group.ExpirationDate)
                .Select(x => new MobileSkuLocationResponse(x.LocationCode,
                    x.Group.ExpirationDate, x.AvailableQuantity,
                    x.AccountingQuantity)).ToArray()));
    }
}
