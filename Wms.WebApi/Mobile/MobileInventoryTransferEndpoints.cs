using Wms.Application.Inventory.Transfers;
using Wms.Application.Warehouses;
using Wms.Common;
using Wms.Contracts.Mobile.V1;
using Wms.Domain.Enums;

namespace Wms.WebApi.Mobile;

internal static class MobileInventoryTransferEndpoints
{
    public static IEndpointRouteBuilder MapMobileInventoryTransferEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(MobileApiRoutes.Base)
            .WithTags("Mobile Inventory Transfers")
            .RequireAuthorization(MobileAuthorization.WarehouseOperatorPolicy);

        group.MapGet("/warehouses", ListWarehousesAsync)
            .Produces<IReadOnlyList<MobileWarehouseResponse>>();

        group.MapGet("/inventory-transfers", ListTransfersAsync)
            .Produces<IReadOnlyList<MobileInventoryTransferSummaryResponse>>();

        return endpoints;
    }

    private static async Task<IResult> ListWarehousesAsync(
        WarehouseService warehouseService,
        CancellationToken ct)
    {
        var result = await warehouseService.ListAsync(new ListQuery
        {
            ExcludeDeleted = true,
            SortBy = "Name",
            Take = 100
        }, ct);

        return TypedResults.Ok<IReadOnlyList<MobileWarehouseResponse>>(
            result.Items
                .Select(x => new MobileWarehouseResponse(x.Id, x.Name ?? string.Empty))
                .ToList());
    }

    private static async Task<IResult> ListTransfersAsync(
        Guid warehouseId,
        InventoryTransferQueryService queryService,
        CancellationToken ct)
    {
        if (warehouseId == Guid.Empty)
        {
            return Results.Json(
                new MobileProblemResponse("warehouse_required", "Выберите склад."),
                statusCode: StatusCodes.Status400BadRequest);
        }

        var transfers = await queryService.ListActiveAsync(warehouseId, 50, ct);
        return TypedResults.Ok<IReadOnlyList<MobileInventoryTransferSummaryResponse>>(
            transfers.Select(x => new MobileInventoryTransferSummaryResponse(
                x.Id,
                x.Number,
                x.Date,
                x.WarehouseId,
                x.Warehouse?.Name ?? string.Empty,
                MapStatus(x.Status),
                x.CreatedAtUtc,
                x.UpdatedAtUtc)).ToList());
    }

    private static MobileInventoryTransferStatus MapStatus(InventoryTransferStatus status) =>
        status switch
        {
            InventoryTransferStatus.Draft => MobileInventoryTransferStatus.Draft,
            InventoryTransferStatus.InProgress => MobileInventoryTransferStatus.InProgress,
            InventoryTransferStatus.Completed => MobileInventoryTransferStatus.Completed,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
        };
}
