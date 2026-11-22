using System.Security.Claims;
using Wms.Application.Commands;
using Wms.Application.Inventory.Counts;
using Wms.Common;
using Wms.Contracts.Mobile.V1;
using Wms.Data;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.WebApi.Mobile;

internal static class MobileInventoryCountEndpoints
{
    private const int SkuSearchResultLimit = 10;

    public static IEndpointRouteBuilder MapMobileInventoryCountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(MobileApiRoutes.Base + "/inventory-counts")
            .WithTags("Mobile Inventory Counts")
            .RequireAuthorization(policy => policy.RequireRole(ApplicationRoles.Operator));

        group.MapGet("", ListDraftsAsync)
            .WithMobileResponses<IReadOnlyList<MobileInventoryCountSummaryResponse>>();
        group.MapGet("/{inventoryCountId:guid}", GetAsync)
            .WithMobileResponses<MobileInventoryCountDetailsResponse>();
        group.MapPost("/start", StartAsync)
            .WithMobileResponses<MobileInventoryCountDetailsResponse>();
        group.MapPost("/{inventoryCountId:guid}/sku/scan", IncrementSkuAsync)
            .WithMobileResponses<MobileInventoryCountScanResponse>();
        group.MapGet("/{inventoryCountId:guid}/skus", SearchSkusAsync)
            .WithMobileResponses<IReadOnlyList<MobileInventoryCountSkuSearchResponse>>();
        group.MapPost("/{inventoryCountId:guid}/sku-quantity", SetSkuQuantityAsync)
            .WithMobileResponses<MobileInventoryCountDetailsResponse>();
        group.MapPost("/{inventoryCountId:guid}/items/{itemId:guid}/quantity", SetItemQuantityAsync)
            .WithMobileResponses<MobileInventoryCountDetailsResponse>();
        group.MapPost("/{inventoryCountId:guid}/items/{itemId:guid}/remove", RemoveItemAsync)
            .WithMobileResponses<MobileInventoryCountDetailsResponse>();
        group.MapPost("/{inventoryCountId:guid}/post", PostAsync)
            .WithMobileResponses<MobileInventoryCountDetailsResponse>();
        group.MapPost("/{inventoryCountId:guid}/delete", DeleteDraftAsync)
            .WithMobileResponses<MobileInventoryCountDeletedResponse>();
        return endpoints;
    }

    private static async Task<IResult> ListDraftsAsync(
        Guid warehouseId,
        InventoryCountQueryService queryService,
        CancellationToken ct)
    {
        if (warehouseId == Guid.Empty)
            return MobileEndpointResults.CommandProblem(OperationError.Invalid("Выберите склад."));

        var counts = await queryService.ListDraftsAsync(warehouseId, 50, ct);
        return TypedResults.Ok<IReadOnlyList<MobileInventoryCountSummaryResponse>>(
            counts.Select(MapSummary).ToList());
    }

    private static async Task<IResult> GetAsync(
        Guid inventoryCountId,
        InventoryCountQueryService queryService,
        CancellationToken ct)
    {
        var count = await queryService.GetAsync(inventoryCountId, ct);
        return count is null
            ? MobileEndpointResults.CommandProblem(
                OperationError.NotFound($"Инвентаризация '{inventoryCountId}' не найдена."))
            : TypedResults.Ok(MapDetails(count));
    }

    private static async Task<IResult> StartAsync(
        MobileStartInventoryCountRequest request,
        ClaimsPrincipal principal,
        InventoryCountQueryService queryService,
        InventoryCountCommandService commandService,
        CancellationToken ct)
    {
        var userId = GetUserId(principal);
        if (userId is null)
            return TypedResults.Unauthorized();
        var result = await commandService.StartAsync(
            new StartInventoryCountCommand(request.WarehouseId, Guid.Empty, request.StorageLocationBarcode),
            new CommandContext(request.ClientRequestId, userId),
            ct);
        return await DetailsResultAsync(result, queryService, ct);
    }

    private static async Task<IResult> IncrementSkuAsync(
        Guid inventoryCountId,
        MobileIncrementInventoryCountSkuRequest request,
        ClaimsPrincipal principal,
        InventoryCountCommandService commandService,
        InventoryCountQueryService queryService,
        CancellationToken ct)
    {
        var userId = GetUserId(principal);
        if (userId is null)
            return TypedResults.Unauthorized();

        var result = await commandService.IncrementSkuAsync(
            new IncrementInventoryCountSkuCommand(inventoryCountId, request.Barcode,
                request.ExpirationDate, request.ConfirmedSkuId),
            new CommandContext(request.ClientRequestId, userId),
            ct);
        var confirmation = result.Error as InventoryCountExpirationRequired;
        if (!result.IsSuccess && confirmation is null)
            return MobileEndpointResults.CommandProblem(result.Error!);
        var count = await queryService.GetAsync(inventoryCountId, ct);
        if (count is null)
            return MobileEndpointResults.CommandProblem(OperationError.NotFound("Инвентаризация не найдена."));
        var details = MapDetails(count);
        if (confirmation is not null)
            return TypedResults.Ok(new MobileInventoryCountScanResponse(details, null,
                new MobileInventoryCountSkuConfirmation(confirmation.SkuId, confirmation.SkuName)));
        // A replay can follow the later removal of an unexpected row.
        var item = details.Items.SingleOrDefault(x => x.Id == result.Value);
        return TypedResults.Ok(new MobileInventoryCountScanResponse(details, item));
    }

    private static async Task<IResult> SearchSkusAsync(
        Guid inventoryCountId,
        string query,
        InventoryCountQueryService queryService,
        CancellationToken ct)
    {
        // One extra item tells the mobile UI that more matches exist than it displays.
        var result = await queryService.SearchSkusAsync(
            inventoryCountId,
            query,
            SkuSearchResultLimit + 1,
            ct);
        if (!result.IsSuccess)
            return MobileEndpointResults.CommandProblem(result.Error!);
        return TypedResults.Ok<IReadOnlyList<MobileInventoryCountSkuSearchResponse>>(
            result.Value!.Select(x => new MobileInventoryCountSkuSearchResponse(
                x.Id,
                x.Code,
                x.Name,
                x.UnitOfMeasure,
                x.IsExactMatch,
                x.RequiresExpiration)).ToList());
    }

    private static async Task<IResult> SetSkuQuantityAsync(
        Guid inventoryCountId,
        MobileSetInventoryCountSkuQuantityRequest request,
        ClaimsPrincipal principal,
        InventoryCountCommandService commandService,
        InventoryCountQueryService queryService,
        CancellationToken ct)
    {
        var userId = GetUserId(principal);
        if (userId is null)
            return TypedResults.Unauthorized();
        var result = await commandService.SetSkuCountedQuantityAsync(
            new SetInventoryCountSkuQuantityCommand(inventoryCountId, request.StockKeepingUnitId,
                request.CountedQuantity, request.ExpirationDate, request.ExpirationConfirmed),
            new CommandContext(request.ClientRequestId, userId),
            ct);
        return await DetailsResultAsync(result, queryService, ct, inventoryCountId);
    }

    private static async Task<IResult> SetItemQuantityAsync(
        Guid inventoryCountId,
        Guid itemId,
        MobileSetInventoryCountItemQuantityRequest request,
        ClaimsPrincipal principal,
        InventoryCountCommandService commandService,
        InventoryCountQueryService queryService,
        CancellationToken ct)
    {
        var userId = GetUserId(principal);
        if (userId is null)
            return TypedResults.Unauthorized();
        var result = await commandService.SetCountedQuantityAsync(
            new SetInventoryCountQuantityCommand(inventoryCountId, itemId, request.CountedQuantity,
                request.ExpirationConfirmed),
            new CommandContext(request.ClientRequestId, userId),
            ct);
        return await DetailsResultAsync(result, queryService, ct, inventoryCountId);
    }

    private static async Task<IResult> RemoveItemAsync(
        Guid inventoryCountId,
        Guid itemId,
        MobileInventoryCountCommandRequest request,
        ClaimsPrincipal principal,
        InventoryCountCommandService commandService,
        InventoryCountQueryService queryService,
        CancellationToken ct)
    {
        var userId = GetUserId(principal);
        if (userId is null)
            return TypedResults.Unauthorized();
        var result = await commandService.RemoveUnexpectedItemAsync(
            new RemoveInventoryCountItemCommand(inventoryCountId, itemId),
            new CommandContext(request.ClientRequestId, userId),
            ct);
        return await DetailsResultAsync(result, queryService, ct, inventoryCountId);
    }

    private static async Task<IResult> PostAsync(
        Guid inventoryCountId,
        MobileInventoryCountCommandRequest request,
        ClaimsPrincipal principal,
        InventoryCountCommandService commandService,
        InventoryCountQueryService queryService,
        CancellationToken ct)
    {
        var userId = GetUserId(principal);
        if (userId is null)
            return TypedResults.Unauthorized();
        var result = await commandService.PostAsync(
            inventoryCountId,
            new CommandContext(request.ClientRequestId, userId),
            ct);
        return await DetailsResultAsync(result, queryService, ct, inventoryCountId);
    }

    private static async Task<IResult> DeleteDraftAsync(
        Guid inventoryCountId,
        MobileInventoryCountCommandRequest request,
        ClaimsPrincipal principal,
        InventoryCountCommandService commandService,
        CancellationToken ct)
    {
        var userId = GetUserId(principal);
        if (userId is null)
            return TypedResults.Unauthorized();
        var result = await commandService.DeleteDraftAsync(
            inventoryCountId,
            new CommandContext(request.ClientRequestId, userId),
            ct);
        return result.IsSuccess
            ? TypedResults.Ok(new MobileInventoryCountDeletedResponse(inventoryCountId))
            : MobileEndpointResults.CommandProblem(result.Error!);
    }

    private static async Task<IResult> DetailsResultAsync(
        OperationResult<Guid> result,
        InventoryCountQueryService queryService,
        CancellationToken ct,
        Guid? inventoryCountId = null)
    {
        if (!result.IsSuccess)
            return MobileEndpointResults.CommandProblem(result.Error!);
        var count = await queryService.GetAsync(inventoryCountId ?? result.Value, ct);
        return count is null
            ? MobileEndpointResults.CommandProblem(OperationError.NotFound("Инвентаризация не найдена."))
            : TypedResults.Ok(MapDetails(count));
    }

    private static MobileInventoryCountDetailsResponse MapDetails(InventoryCount count) => new(
        MapSummary(count),
        count.Items
            .OrderBy(x => x.LineNumber)
            .Select(x => new MobileInventoryCountItemResponse(
                x.Id,
                x.StockKeepingUnitId,
                x.StockKeepingUnit?.Code ?? string.Empty,
                x.StockKeepingUnit?.Name ?? string.Empty,
                x.StockKeepingUnit?.BaseUnitOfMeasure?.Description,
                x.ExpectedQuantity,
                x.CountedQuantity,
                x.DifferenceQuantity,
                x.IsExpected,
                x.ExpirationDate,
                InventoryCountCommandService.RequiresExpiration(count, x.StockKeepingUnit!, x.ExpirationDate),
                x.ExpirationConfirmed))
            .ToList());

    private static MobileInventoryCountSummaryResponse MapSummary(InventoryCount count)
    {
        var location = count.StorageLocation
            ?? throw new InvalidOperationException("Инвентаризация не содержит ячейку.");
        var zone = location.Zone
            ?? throw new InvalidOperationException("Ячейка инвентаризации не содержит зону.");
        return new MobileInventoryCountSummaryResponse(
            count.Id,
            count.Number,
            count.Date,
            count.WarehouseId,
            count.Warehouse?.Name ?? string.Empty,
            new MobileStorageLocationResponse(
                location.Id,
                location.Name,
                location.DisplayAddress,
                count.WarehouseId,
                count.Warehouse?.Name ?? string.Empty,
                zone.Id,
                zone.Name,
                zone.Type == ZoneType.Reserve ? MobileStorageLocationContext.Reserve : MobileStorageLocationContext.Storage),
            count.Status == InventoryCountStatus.Draft
                ? MobileInventoryCountStatus.Draft
                : MobileInventoryCountStatus.Posted,
            count.Items.Count,
            count.Items.Count(x => x.IsCounted),
            count.CreatedAtUtc,
            count.UpdatedAtUtc,
            count.PostedAtUtc);
    }

    private static string? GetUserId(ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.NameIdentifier);
}
