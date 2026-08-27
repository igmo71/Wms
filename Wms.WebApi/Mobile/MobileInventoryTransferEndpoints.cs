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

        group.MapGet(
                "/inventory-transfers/by-transit-location/{transitStorageLocationId:guid}",
                GetTransferByTransitStorageLocationAsync)
            .Produces<MobileInventoryTransferSummaryResponse>()
            .Produces(StatusCodes.Status204NoContent);

        group.MapGet("/inventory-transfers/{transferId:guid}", GetTransferAsync)
            .Produces<MobileInventoryTransferDetailsResponse>()
            .Produces<MobileProblemResponse>(StatusCodes.Status404NotFound);

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

        group.MapGet(
                "/inventory-transfers/{transferId:guid}/direct/skus",
                SearchDirectSkusAsync)
            .Produces<IReadOnlyList<MobileDirectTransferSkuSearchResponse>>()
            .Produces<MobileProblemResponse>(StatusCodes.Status404NotFound)
            .Produces<MobileProblemResponse>(StatusCodes.Status422UnprocessableEntity);

        group.MapPost(
                "/inventory-transfers/{transferId:guid}/transit/sku/resolve",
                ResolveTransitSkuAsync)
            .Produces<MobileDirectTransferSkuResponse>()
            .Produces<MobileProblemResponse>(StatusCodes.Status404NotFound)
            .Produces<MobileProblemResponse>(StatusCodes.Status422UnprocessableEntity);

        group.MapGet(
                "/inventory-transfers/{transferId:guid}/transit/skus",
                SearchTransitSkusAsync)
            .Produces<IReadOnlyList<MobileDirectTransferSkuSearchResponse>>()
            .Produces<MobileProblemResponse>(StatusCodes.Status404NotFound)
            .Produces<MobileProblemResponse>(StatusCodes.Status422UnprocessableEntity);

        group.MapPost(
                "/inventory-transfers/{transferId:guid}/direct-movements",
                MoveDirectAsync)
            .Produces<MobileMoveDirectInventoryTransferResponse>()
            .Produces<MobileProblemResponse>(StatusCodes.Status404NotFound)
            .Produces<MobileProblemResponse>(StatusCodes.Status409Conflict)
            .Produces<MobileProblemResponse>(StatusCodes.Status422UnprocessableEntity);

        group.MapPost(
                "/inventory-transfers/{transferId:guid}/pick-to-transit",
                PickToTransitAsync)
            .Produces<MobileTransitInventoryTransferMovementResponse>()
            .Produces<MobileProblemResponse>(StatusCodes.Status404NotFound)
            .Produces<MobileProblemResponse>(StatusCodes.Status409Conflict)
            .Produces<MobileProblemResponse>(StatusCodes.Status422UnprocessableEntity);

        group.MapPost(
                "/inventory-transfers/{transferId:guid}/put-from-transit",
                PutFromTransitAsync)
            .Produces<MobileTransitInventoryTransferMovementResponse>()
            .Produces<MobileProblemResponse>(StatusCodes.Status404NotFound)
            .Produces<MobileProblemResponse>(StatusCodes.Status409Conflict)
            .Produces<MobileProblemResponse>(StatusCodes.Status422UnprocessableEntity);

        group.MapPost(
                "/inventory-transfers/{transferId:guid}/complete",
                CompleteTransferAsync)
            .Produces<MobileCompleteInventoryTransferResponse>()
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
            request.TransitStorageLocationId,
            request.ClientRequestId,
            userId,
            ct);
        if (!result.IsSuccess)
        {
            return MobileEndpointResults.CommandProblem(result.Error!);
        }

        var transfer = await queryService.GetAsync(result.Value, ct);
        if (transfer is null)
        {
            throw new InvalidOperationException(
                "Созданное мобильной командой перемещение не найдено.");
        }

        return TypedResults.Ok(MapTransfer(transfer));
    }

    private static async Task<IResult> GetTransferByTransitStorageLocationAsync(
        Guid transitStorageLocationId,
        InventoryTransferQueryService queryService,
        CancellationToken ct)
    {
        var transfer = await queryService.GetActiveByTransitStorageLocationAsync(
            transitStorageLocationId,
            ct);
        return transfer is null
            ? TypedResults.NoContent()
            : TypedResults.Ok(MapTransfer(transfer));
    }

    private static async Task<IResult> GetTransferAsync(
        Guid transferId,
        InventoryTransferQueryService queryService,
        CancellationToken ct)
    {
        var transfer = await queryService.GetAsync(transferId, ct);
        if (transfer is null)
        {
            return MobileEndpointResults.CommandProblem(
                OperationError.NotFound($"Перемещение '{transferId}' не найдено."));
        }

        var movements = await queryService.GetMovementsAsync(transferId, ct);
        var mobileMovements = new List<MobileInventoryTransferMovementResponse>(movements.Count);
        foreach (var movement in movements.OrderByDescending(x => x.RecorderLineNumber))
        {
            if (movement.SourceStorageLocation?.Zone is null
                || movement.DestinationStorageLocation?.Zone is null
                || movement.StockKeepingUnit is null
                || movement.RecorderLineNumber is null)
            {
                throw new InvalidOperationException(
                    "История движений мобильного перемещения содержит неполные данные.");
            }

            mobileMovements.Add(new MobileInventoryTransferMovementResponse(
                movement.Id,
                movement.StockKeepingUnitId,
                movement.StockKeepingUnit.Code ?? string.Empty,
                movement.StockKeepingUnit.Name ?? string.Empty,
                movement.StockKeepingUnit.BaseUnitOfMeasure?.Description,
                movement.Quantity,
                MapLocation(movement.SourceStorageLocation),
                MapLocation(movement.DestinationStorageLocation)));
        }

        var transitBalances = await queryService.GetTransitBalancesAsync(transferId, ct);
        return TypedResults.Ok(new MobileInventoryTransferDetailsResponse(
            MapTransfer(transfer),
            mobileMovements,
            transitBalances
                .Select(x => new MobileInventoryTransferSkuBalanceResponse(
                    x.StockKeepingUnit.Id,
                    x.StockKeepingUnit.Code ?? string.Empty,
                    x.StockKeepingUnit.Name ?? string.Empty,
                    x.StockKeepingUnit.BaseUnitOfMeasure?.Description,
                    x.Quantity))
                .ToList()));
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
            return MobileEndpointResults.CommandProblem(skuResult.Error!);
        }

        var sku = skuResult.Value!;
        var quantityResult = await transferQueryService.GetAvailableDirectQuantityAsync(
            transferId,
            request.SourceStorageLocationId,
            sku.Id,
            ct);
        if (!quantityResult.IsSuccess)
        {
            return MobileEndpointResults.CommandProblem(quantityResult.Error!);
        }

        return TypedResults.Ok(new MobileDirectTransferSkuResponse(
            sku.Id,
            sku.Code ?? string.Empty,
            sku.Name ?? string.Empty,
            sku.BaseUnitOfMeasure?.Description,
            quantityResult.Value));
    }

    private static async Task<IResult> SearchDirectSkusAsync(
        Guid transferId,
        Guid sourceStorageLocationId,
        string query,
        InventoryTransferQueryService transferQueryService,
        CancellationToken ct)
    {
        var result = await transferQueryService.SearchAvailableDirectSkusAsync(
            transferId,
            sourceStorageLocationId,
            query,
            10,
            ct);
        if (!result.IsSuccess)
        {
            return MobileEndpointResults.CommandProblem(result.Error!);
        }

        return TypedResults.Ok<IReadOnlyList<MobileDirectTransferSkuSearchResponse>>(
            result.Value!
                .Select(x => new MobileDirectTransferSkuSearchResponse(
                    x.Id,
                    x.Code,
                    x.Name,
                    x.UnitOfMeasure,
                    x.AvailableQuantity,
                    x.IsExactMatch))
                .ToList());
    }

    private static async Task<IResult> ResolveTransitSkuAsync(
        Guid transferId,
        MobileResolveTransitTransferSkuRequest request,
        StockKeepingUnitService skuService,
        InventoryTransferQueryService transferQueryService,
        CancellationToken ct)
    {
        var skuResult = await skuService.ResolveByBarcodeAsync(request.Barcode, ct);
        if (!skuResult.IsSuccess)
        {
            return MobileEndpointResults.CommandProblem(skuResult.Error!);
        }

        var sku = skuResult.Value!;
        var quantityResult = await transferQueryService.GetAvailableTransitQuantityAsync(
            transferId,
            sku.Id,
            ct);
        if (!quantityResult.IsSuccess)
        {
            return MobileEndpointResults.CommandProblem(quantityResult.Error!);
        }

        return TypedResults.Ok(new MobileDirectTransferSkuResponse(
            sku.Id,
            sku.Code ?? string.Empty,
            sku.Name ?? string.Empty,
            sku.BaseUnitOfMeasure?.Description,
            quantityResult.Value));
    }

    private static async Task<IResult> SearchTransitSkusAsync(
        Guid transferId,
        string query,
        InventoryTransferQueryService transferQueryService,
        CancellationToken ct)
    {
        var result = await transferQueryService.SearchAvailableTransitSkusAsync(
            transferId,
            query,
            10,
            ct);
        if (!result.IsSuccess)
        {
            return MobileEndpointResults.CommandProblem(result.Error!);
        }

        return TypedResults.Ok<IReadOnlyList<MobileDirectTransferSkuSearchResponse>>(
            result.Value!
                .Select(x => new MobileDirectTransferSkuSearchResponse(
                    x.Id,
                    x.Code,
                    x.Name,
                    x.UnitOfMeasure,
                    x.AvailableQuantity,
                    x.IsExactMatch))
                .ToList());
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
            return MobileEndpointResults.CommandProblem(result.Error!);
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

    private static async Task<IResult> PickToTransitAsync(
        Guid transferId,
        MobilePickToTransitRequest request,
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

        var result = await commandService.PickToTransitAsync(
            transferId,
            request.SourceStorageLocationId,
            request.StockKeepingUnitId,
            request.Quantity,
            request.ClientRequestId,
            userId,
            ct);
        return await TransitMovementResultAsync(result, transferId, queryService, ct);
    }

    private static async Task<IResult> PutFromTransitAsync(
        Guid transferId,
        MobilePutFromTransitRequest request,
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

        var result = await commandService.PutFromTransitAsync(
            transferId,
            request.DestinationStorageLocationId,
            request.StockKeepingUnitId,
            request.Quantity,
            request.ClientRequestId,
            userId,
            ct);
        return await TransitMovementResultAsync(result, transferId, queryService, ct);
    }

    private static async Task<IResult> TransitMovementResultAsync(
        OperationResult<Guid> result,
        Guid transferId,
        InventoryTransferQueryService queryService,
        CancellationToken ct)
    {
        if (!result.IsSuccess)
        {
            return MobileEndpointResults.CommandProblem(result.Error!);
        }

        var transfer = await queryService.GetAsync(transferId, ct);
        if (transfer is null)
        {
            throw new InvalidOperationException(
                "Результат мобильного транзитного перемещения не найден.");
        }

        return TypedResults.Ok(new MobileTransitInventoryTransferMovementResponse(
            result.Value,
            transferId,
            MapStatus(transfer.Status)));
    }

    private static MobileInventoryMovementLocationResponse MapLocation(
        Wms.Domain.StorageLocation location) => new(
            location.Id,
            $"{location.Zone!.Code}-{location.Code}",
            location.Name);

    private static async Task<IResult> CompleteTransferAsync(
        Guid transferId,
        MobileCompleteInventoryTransferRequest request,
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

        var result = await commandService.CompleteAsync(
            transferId,
            request.ClientRequestId,
            userId,
            ct);
        if (!result.IsSuccess)
        {
            return MobileEndpointResults.CommandProblem(result.Error!);
        }

        var transfer = await queryService.GetAsync(result.Value, ct);
        if (transfer?.CompletedAtUtc is null)
        {
            throw new InvalidOperationException(
                "Результат завершения мобильного перемещения не найден.");
        }

        return TypedResults.Ok(new MobileCompleteInventoryTransferResponse(
            transfer.Id,
            MapStatus(transfer.Status),
            transfer.CompletedAtUtc.Value));
    }

    private static MobileInventoryTransferSummaryResponse MapTransfer(
        Wms.Domain.InventoryTransfer transfer)
    {
        MobileStorageLocationResponse? transitLocation = null;
        if (transfer.TransitStorageLocation?.Zone is { } zone)
        {
            transitLocation = new MobileStorageLocationResponse(
                transfer.TransitStorageLocation.Id,
                transfer.TransitStorageLocation.Name,
                $"{zone.Code}-{transfer.TransitStorageLocation.Code}",
                transfer.WarehouseId,
                transfer.Warehouse?.Name ?? string.Empty,
                zone.Id,
                zone.Name,
                MobileStorageLocationContext.Transit);
        }

        return new MobileInventoryTransferSummaryResponse(
            transfer.Id,
            transfer.Number,
            transfer.Date,
            transfer.WarehouseId,
            transfer.Warehouse?.Name ?? string.Empty,
            MapStatus(transfer.Status),
            transfer.CreatedAtUtc,
            transfer.UpdatedAtUtc,
            transitLocation);
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
