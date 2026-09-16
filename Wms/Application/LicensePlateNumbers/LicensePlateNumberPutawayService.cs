using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Wms.Application.Commands;
using Wms.Application.Inventory.Movements;
using Wms.Application.ReceivingOrders;
using Wms.Common;
using Wms.Data;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.LicensePlateNumbers;

public sealed class LicensePlateNumberPutawayService(
    CommandExecutor commandExecutor,
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    InventoryPostingService inventoryPostingService)
{
    private const string PutawayCommandType = "lpn.putaway";

    public async Task<OperationResult<LicensePlateNumberPutawayDetails>> GetByCodeAsync(
        string code,
        CancellationToken ct = default)
    {
        var normalized = code?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
            return OperationError.Invalid("Отсканируйте LPN.");

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        var lpn = await LoadAsync(dbContext, x => x.Code == normalized, true, ct);
        return lpn is null
            ? OperationError.NotFound("LPN не найден.")
            : Map(lpn);
    }

    public async Task<OperationResult<LicensePlateNumberPutawayDetails>> GetAsync(
        Guid lpnId,
        CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        var lpn = await LoadAsync(dbContext, x => x.Id == lpnId, true, ct);
        return lpn is null
            ? OperationError.NotFound("LPN не найден.")
            : Map(lpn);
    }

    public async Task<OperationResult<LicensePlateNumberPutawayItem>> ResolveSkuAsync(
        Guid lpnId,
        string barcode,
        CancellationToken ct = default)
    {
        var normalized = barcode?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return OperationError.Invalid("Отсканируйте товар.");

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        var lpn = await LoadAsync(dbContext, x => x.Id == lpnId, true, ct);
        if (lpn is null)
            return OperationError.NotFound("LPN не найден.");

        var details = Map(lpn);
        var matchedSkuIds = await dbContext.StockKeepingUnits
            .Where(x => x.Code == normalized || x.Barcodes.Any(b => b.Value == normalized))
            .Select(x => x.Id)
            .ToListAsync(ct);
        var matches = details.Items
            .Where(x => x.RemainingQuantity > 0 && matchedSkuIds.Contains(x.StockKeepingUnitId))
            .ToList();
        return matches.Count switch
        {
            0 => OperationError.NotFound("В выбранном LPN нет этого товара с остатком к размещению."),
            1 => matches[0],
            _ => OperationError.Conflict("Штрихкод соответствует нескольким товарам LPN.")
        };
    }

    public Task<OperationResult<Guid>> PutawayAsync(
        PutawayLicensePlateNumberCommand command,
        CommandContext context,
        CancellationToken ct = default) =>
        commandExecutor.ExecuteAsync(
            PutawayCommandType,
            context.RequestId,
            CommandExecutor.ComputeHash(
                $"{command.LicensePlateNumberId:N}|{command.LicensePlateNumberItemId:N}|{command.DestinationStorageLocationId:N}|{command.Quantity.ToString("G29", CultureInfo.InvariantCulture)}"),
            context.UserId,
            (db, token) => PutawayCoreAsync(db, command, context.UserId, token),
            ct);

    private async Task<OperationResult<Guid>> PutawayCoreAsync(
        ApplicationDbContext dbContext,
        PutawayLicensePlateNumberCommand command,
        string userId,
        CancellationToken ct)
    {
        var lpn = await LoadAsync(dbContext, x => x.Id == command.LicensePlateNumberId, false, ct);
        if (lpn is null)
            return OperationError.NotFound("LPN не найден.");
        if (lpn.ReceivingOrder is not { Status: ReceivingOrderStatus.Received } order)
            return OperationError.Invalid("Размещение запрещено до завершения приходного ордера.");
        if (order.ReceivingLocationId is null)
            return OperationError.Invalid("Для приходного ордера не назначена позиция приёмки.");

        var item = lpn.Items.SingleOrDefault(x => x.Id == command.LicensePlateNumberItemId && !x.IsDeleted);
        if (item is null)
            return OperationError.NotFound("Строка состава LPN не найдена.");
        var orderItem = order.Items.SingleOrDefault(x => x.StockKeepingUnitId == item.StockKeepingUnitId);
        if (orderItem is null)
            return OperationError.Invalid("SKU LPN отсутствует в принятом ордере.");

        var movementId = Guid.NewGuid();
        var movementResult = InventoryMovement.Create(
            movementId,
            order.WarehouseId,
            order.ReceivingLocationId,
            command.DestinationStorageLocationId,
            item.StockKeepingUnitId,
            command.Quantity,
            DateTimeOffset.UtcNow,
            RecorderType.ReceivingOrder,
            order.Id,
            orderItem.LineNumber,
            userId);
        if (!movementResult.IsSuccess)
            return movementResult.Error!;

        var putawayResult = lpn.CreatePutaway(
            item.Id,
            command.DestinationStorageLocationId,
            command.Quantity,
            movementId,
            userId,
            DateTimeOffset.UtcNow);
        if (!putawayResult.IsSuccess)
            return putawayResult.Error!;

        var movement = movementResult.Value!;
        var routeResult = await ReceivingOrderLocationPolicy.ValidatePutawayRoutesAsync(
            dbContext, order, [movement], ct);
        if (!routeResult.IsSuccess)
            return routeResult.Error!;

        dbContext.InventoryMovements.Add(movement);
        dbContext.LicensePlateNumberPutaways.Add(putawayResult.Value!);
        var postingResult = await inventoryPostingService.PostInventoryMovementsAsync(
            [movement], dbContext, ct);
        if (!postingResult.IsSuccess)
            return postingResult.Error!;

        return putawayResult.Value!.Id;
    }

    private static Task<LicensePlateNumber?> LoadAsync(
        ApplicationDbContext dbContext,
        System.Linq.Expressions.Expression<Func<LicensePlateNumber, bool>> predicate,
        bool noTracking,
        CancellationToken ct)
    {
        IQueryable<LicensePlateNumber> query = dbContext.LicensePlateNumbers
            .Include(x => x.ReceivingOrder).ThenInclude(x => x!.Items)
            .Include(x => x.Items).ThenInclude(x => x.StockKeepingUnit).ThenInclude(x => x.BaseUnitOfMeasure)
            .Include(x => x.Items).ThenInclude(x => x.StockKeepingUnit).ThenInclude(x => x.Barcodes)
            .Include(x => x.Putaways).ThenInclude(x => x.DestinationStorageLocation).ThenInclude(x => x.Zone)
            .Include(x => x.Putaways).ThenInclude(x => x.StockKeepingUnit);
        if (noTracking)
            query = query.AsNoTracking();
        return query.SingleOrDefaultAsync(predicate, ct);
    }

    private static LicensePlateNumberPutawayDetails Map(LicensePlateNumber lpn)
    {
        var placedByItem = lpn.Putaways
            .GroupBy(x => x.LicensePlateNumberItemId)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.Quantity));
        var items = lpn.Items
            .Where(x => !x.IsDeleted)
            .OrderBy(x => x.StockKeepingUnit.Name)
            .Select(x =>
            {
                var placed = placedByItem.GetValueOrDefault(x.Id);
                return new LicensePlateNumberPutawayItem(
                    x.Id, x.StockKeepingUnitId, x.StockKeepingUnit.Code ?? string.Empty,
                    x.StockKeepingUnit.Name ?? string.Empty,
                    x.StockKeepingUnit.BaseUnitOfMeasure?.Description
                        ?? x.StockKeepingUnit.BaseUnitOfMeasure?.Abbreviation,
                    x.Quantity, placed, x.Quantity - placed);
            })
            .ToList();
        var blockedReason = lpn.Status != LicensePlateNumberStatus.Closed
            ? "Размещать можно только закрытый LPN."
            : lpn.ReceivingOrder?.Status != ReceivingOrderStatus.Received
                ? "Размещение запрещено до завершения приходного ордера."
                : null;
        return new LicensePlateNumberPutawayDetails(
            lpn.Id,
            lpn.Code,
            lpn.ReceivingOrder?.Number ?? string.Empty,
            lpn.ReceivingOrder?.WarehouseId ?? Guid.Empty,
            blockedReason is null && items.Any(x => x.RemainingQuantity > 0),
            blockedReason,
            items.All(x => x.RemainingQuantity == 0),
            items,
            lpn.Putaways.OrderByDescending(x => x.CreatedAtUtc).Select(x =>
                new LicensePlateNumberPutawayHistoryItem(
                    x.Id, x.LicensePlateNumberItemId, x.StockKeepingUnitId,
                    x.StockKeepingUnit.Name ?? string.Empty, x.Quantity,
                    $"{x.DestinationStorageLocation.Zone.Code}-{x.DestinationStorageLocation.Code}",
                    x.DestinationStorageLocation.Name ?? string.Empty,
                    x.CreatedBy, x.CreatedAtUtc, x.InventoryMovementId)).ToList());
    }
}
