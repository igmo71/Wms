using System.Security.Claims;
using Wms.Application.Inventory;
using Wms.Contracts.Mobile.V1;
using Wms.Data;

namespace Wms.WebApi.Mobile;

internal static class MobileStockCorrectionEndpoints
{
    public static IEndpointRouteBuilder MapMobileStockCorrectionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(MobileApiRoutes.StockCorrections)
            .WithTags("Mobile Stock Corrections")
            .RequireAuthorization(MobileAuthorization.PickingWorkerPolicy);
        group.MapGet("/cell", GetCellAsync).WithMobileResponses<MobileStockCorrectionCellResponse>();
        group.MapPost("", PostAsync).WithMobileResponses<MobileCorrectCellStockResponse>();
        return endpoints;
    }

    private static async Task<IResult> GetCellAsync(Guid warehouseId, string barcode,
        StockAvailabilityStore store, CancellationToken ct)
    {
        var result = await store.ReadCorrectionCellAsync(warehouseId, barcode, ct);
        if (!result.IsSuccess)
            return MobileEndpointResults.CommandProblem(result.Error!);

        var cell = result.Value!;
        return TypedResults.Ok(new MobileStockCorrectionCellResponse(warehouseId, cell.Location.Id,
            cell.Location.Code, cell.Location.DisplayAddress, cell.Location.Name, cell.Location.ActiveLock?.Reason,
            cell.Groups.Select(x => new MobileStockCorrectionGroup(x.Sku.Id, x.Sku.Code ?? string.Empty,
                x.Sku.Name ?? string.Empty, x.Sku.BaseUnitOfMeasure?.Name, x.Stock.Group.ExpirationDate,
                x.Stock.AccountingQuantity, x.Stock.ConfirmedReservedQuantity,
                x.Stock.RouteReservations.Sum(r => r.Quantity), x.Stock.BlockedQuantity,
                x.Stock.AvailableQuantity, !x.Sku.DeletionMark)).ToArray()));
    }

    private static async Task<IResult> PostAsync(MobileCorrectCellStockRequest request,
        ClaimsPrincipal principal, CorrectCellStock correction, CancellationToken ct)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
            return TypedResults.Unauthorized();
        var posted = await correction.ExecuteAsync(new(request.ClientRequestId, userId, request.WarehouseId,
            request.LocationId, request.SkuId, request.ExpirationDate, request.QuantityChange), ct);
        return posted.IsSuccess
            ? TypedResults.Ok(new MobileCorrectCellStockResponse(posted.Value))
            : MobileEndpointResults.CommandProblem(posted.Error!);
    }
}
