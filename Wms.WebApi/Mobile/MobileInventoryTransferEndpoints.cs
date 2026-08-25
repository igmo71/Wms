using System.Security.Claims;
using Wms.Application.Inventory.Transfers;
using Wms.Application.StockKeepingUnits;
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

        group.MapPost("/inventory-transfers", CreateTransferAsync)
            .Produces<MobileInventoryTransferSummaryResponse>()
            .Produces<MobileProblemResponse>(StatusCodes.Status400BadRequest)
            .Produces<MobileProblemResponse>(StatusCodes.Status404NotFound)
            .Produces<MobileProblemResponse>(StatusCodes.Status409Conflict)
            .Produces<MobileProblemResponse>(StatusCodes.Status422UnprocessableEntity);

        group.MapPost(
                "/inventory-transfers/{transferId:guid}/direct/sku/resolve",
                ResolveDirectSkuAsync)
            .Produces<MobileDirectTransferSkuResponse>()
            .Produces<MobileProblemResponse>(StatusCodes.Status404NotFound)
            .Produces<MobileProblemResponse>(StatusCodes.Status409Conflict)
            .Produces<MobileProblemResponse>(StatusCodes.Status422UnprocessableEntity);

        group.MapPost(
                "/inventory-transfers/{transferId:guid}/direct-movements",
                MoveDirectAsync)
            .Produces<MobileMoveDirectInventoryTransferResponse>()
            .Produces<MobileProblemResponse>(StatusCodes.Status404NotFound)
            .Produces<MobileProblemResponse>(StatusCodes.Status409Conflict)
            .Produces<MobileProblemResponse>(StatusCodes.Status422UnprocessableEntity);

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
            transfers.Select(MapTransfer).ToList());
    }

    private static async Task<IResult> CreateTransferAsync(
        MobileCreateInventoryTransferRequest request,
        ClaimsPrincipal principal,
        MobileInventoryTransferCommandService commandService,
        InventoryTransferQueryService queryService,
        CancellationToken ct)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
        {
            return TypedResults.Unauthorized();
        }

        var result = await commandService.CreateDraftAsync(
            request.WarehouseId,
            request.ClientRequestId,
            userId,
            ct);
        if (!result.IsSuccess)
        {
            return CommandProblem(result.Error!);
        }

        var transfer = await queryService.GetAsync(result.Value, ct);
        if (transfer is null)
        {
            throw new InvalidOperationException(
                "Созданное мобильной командой перемещение не найдено.");
        }

        return TypedResults.Ok(MapTransfer(transfer));
    }

    private static async Task<IResult> ResolveDirectSkuAsync(
        Guid transferId,
        MobileResolveDirectTransferSkuRequest request,
        StockKeepingUnitService skuService,
        InventoryTransferQueryService transferQueryService,
        CancellationToken ct)
    {
        var skuResult = await skuService.ResolveByBarcodeAsync(request.Barcode, ct);
        if (!skuResult.IsSuccess)
        {
            return CommandProblem(skuResult.Error!);
        }

        var sku = skuResult.Value!;
        var quantityResult = await transferQueryService.GetAvailableDirectQuantityAsync(
            transferId,
            request.SourceStorageLocationId,
            sku.Id,
            ct);
        if (!quantityResult.IsSuccess)
        {
            return CommandProblem(quantityResult.Error!);
        }

        return TypedResults.Ok(new MobileDirectTransferSkuResponse(
            sku.Id,
            sku.Code ?? string.Empty,
            sku.Name ?? string.Empty,
            sku.BaseUnitOfMeasure?.Name,
            quantityResult.Value));
    }

    private static async Task<IResult> MoveDirectAsync(
        Guid transferId,
        MobileMoveDirectInventoryTransferRequest request,
        ClaimsPrincipal principal,
        MobileInventoryTransferCommandService commandService,
        InventoryTransferQueryService queryService,
        CancellationToken ct)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
        {
            return TypedResults.Unauthorized();
        }

        var result = await commandService.MoveDirectAsync(
            transferId,
            request.SourceStorageLocationId,
            request.DestinationStorageLocationId,
            request.StockKeepingUnitId,
            request.Quantity,
            request.ClientRequestId,
            userId,
            ct);
        if (!result.IsSuccess)
        {
            return CommandProblem(result.Error!);
        }

        var movement = await queryService.GetMovementAsync(transferId, result.Value, ct);
        var transfer = await queryService.GetAsync(transferId, ct);
        if (movement?.SourceStorageLocation?.Zone is null
            || movement.DestinationStorageLocation?.Zone is null
            || movement.StockKeepingUnit is null
            || movement.RecorderLineNumber is null
            || movement.PostedAtUtc is null
            || transfer is null)
        {
            throw new InvalidOperationException(
                "Результат мобильного прямого перемещения не найден.");
        }

        return TypedResults.Ok(new MobileMoveDirectInventoryTransferResponse(
            movement.Id,
            transferId,
            movement.RecorderLineNumber.Value,
            movement.StockKeepingUnitId,
            movement.StockKeepingUnit.Code ?? string.Empty,
            movement.StockKeepingUnit.Name ?? string.Empty,
            movement.StockKeepingUnit.BaseUnitOfMeasure?.Name,
            movement.Quantity,
            MapLocation(movement.SourceStorageLocation),
            MapLocation(movement.DestinationStorageLocation),
            movement.PostedAtUtc.Value,
            MapStatus(transfer.Status)));
    }

    private static MobileInventoryMovementLocationResponse MapLocation(
        Wms.Domain.StorageLocation location) => new(
            location.Id,
            $"{location.Zone!.Code}-{location.Code}",
            location.Name);

    private static MobileInventoryTransferSummaryResponse MapTransfer(
        Wms.Domain.InventoryTransfer transfer) => new(
            transfer.Id,
            transfer.Number,
            transfer.Date,
            transfer.WarehouseId,
            transfer.Warehouse?.Name ?? string.Empty,
            MapStatus(transfer.Status),
            transfer.CreatedAtUtc,
            transfer.UpdatedAtUtc);

    private static IResult CommandProblem(OperationError error)
    {
        var (statusCode, code) = error.Type switch
        {
            OperationErrorType.NotFound => (StatusCodes.Status404NotFound, "resource_not_found"),
            OperationErrorType.Conflict => (StatusCodes.Status409Conflict, "request_conflict"),
            OperationErrorType.Invalid => (StatusCodes.Status422UnprocessableEntity, "invalid_command"),
            _ => (StatusCodes.Status400BadRequest, "command_failed")
        };

        return Results.Json(
            new MobileProblemResponse(code, error.Message),
            statusCode: statusCode);
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
