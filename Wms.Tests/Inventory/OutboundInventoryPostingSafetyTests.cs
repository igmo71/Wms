using Microsoft.EntityFrameworkCore;
using Wms.Application.CommandExecution;
using Wms.Application.Inventory;
using Wms.Common;
using Wms.Data;
using Wms.Domain;
using Wms.Domain.Enums;
using Wms.Tests.Infrastructure;
using static Wms.Tests.Infrastructure.TestSupport;

namespace Wms.Tests.Inventory;

[Collection(SqlDatabaseCollection.Name)]
public sealed class OutboundInventoryPostingSafetyTests(SqlDatabase database)
{
    private const string UserId = "posting-worker";
    private const string CommandType = "test.outbound.post";

    [Fact]
    public async Task Issue_posts_own_dated_reservation_once_and_finishes_it()
    {
        var (warehouseId, sourceId, destinationId, skuId) = await SeedAsync(ZoneType.Storage);
        var now = DateTimeOffset.UtcNow;
        var date = DateOnly.FromDateTime(now.UtcDateTime).AddDays(30);
        var assemblyId = Guid.NewGuid();
        var entryId = Guid.NewGuid();
        await using (var db = database.CreateDbContext())
        {
            db.Add(Value(InventoryBalance.Create(Guid.NewGuid(), warehouseId, sourceId, skuId, 10, now, date)));
            db.Add(Value(PickingReservation.Create(entryId, assemblyId, warehouseId,
                sourceId, skuId, date, 10, UserId, now)));
            await db.SaveChangesAsync();
        }
        var movement = Value(InventoryMovement.Create(Guid.NewGuid(), warehouseId, sourceId, null,
            skuId, 10, now, RecorderType.PickingAssembly, assemblyId, 1, UserId, date));
        var requestId = Guid.NewGuid();
        var command = new PostInventoryMovement.Command(movement, InventoryPostingKind.PickingIssue,
            now.AddSeconds(1), PickingEntryId: entryId);

        Assert.Equal(movement.Id, Value(await PostAsync(command, requestId)));
        Assert.Equal(movement.Id, Value(await PostAsync(command, requestId)));

        await using var verify = database.CreateDbContext();
        var reservation = await verify.PickingReservations.SingleAsync(x => x.PickingEntryId == entryId);
        Assert.NotNull(reservation.CompletedAtUtc);
        Assert.Equal(0m, reservation.Quantity);
        Assert.Equal(0m, (await verify.InventoryBalances.SingleAsync(x => x.WarehouseId == warehouseId)).Quantity);
        Assert.False(await verify.InventoryBalances.AnyAsync(x => x.StorageLocationId == destinationId));
        Assert.Equal(date, (await verify.InventoryMovements.SingleAsync(x => x.Id == movement.Id)).ExpirationDate);
        Assert.Equal(date, (await verify.InventoryTurnovers.SingleAsync(x => x.InventoryMovementId == movement.Id)).ExpirationDate);
        Assert.Equal(1, await verify.CommandReceipts.CountAsync(x => x.RequestId == requestId));
    }

    [Fact]
    public async Task Replenishment_posts_found_quantity_but_keeps_hold_and_disputed_remainder()
    {
        var (warehouseId, sourceId, destinationId, skuId) = await SeedAsync(ZoneType.Reserve);
        var now = DateTimeOffset.UtcNow;
        var taskId = Guid.NewGuid();
        var otherSku = new StockKeepingUnit { Id = Guid.NewGuid(), Name = "Other pallet SKU" };
        var shortage = Value(StockShortage.Report(Guid.NewGuid(), warehouseId, sourceId, skuId,
            null, StockShortageOrigin.Replenishment, taskId, 10, 8, null, UserId, now));
        await using (var db = database.CreateDbContext())
        {
            db.AddRange(otherSku, shortage,
                Value(InventoryBalance.Create(Guid.NewGuid(), warehouseId, sourceId, skuId, 10, now)),
                Value(InventoryBalance.Create(Guid.NewGuid(), warehouseId, sourceId, otherSku.Id, 3, now)),
                Value(StorageLocationLock.CreateForReplenishment(sourceId, taskId, now, UserId)));
            await db.SaveChangesAsync();
        }
        var movement = Value(InventoryMovement.Create(Guid.NewGuid(), warehouseId, sourceId, destinationId,
            skuId, 8, now, RecorderType.ReplenishmentTask, taskId, 1, UserId));
        var requestId = Guid.NewGuid();
        var command = new PostInventoryMovement.Command(movement, InventoryPostingKind.Replenishment, now.AddSeconds(1));
        Value(await PostAsync(command, requestId));
        Value(await PostAsync(command, requestId));

        await using var verify = database.CreateDbContext();
        Assert.Equal(2m, (await verify.InventoryBalances.SingleAsync(x => x.StorageLocationId == sourceId
            && x.StockKeepingUnitId == skuId)).Quantity);
        Assert.Equal(8m, (await verify.InventoryBalances.SingleAsync(x => x.StorageLocationId == destinationId)).Quantity);
        Assert.Equal(3m, (await verify.InventoryBalances.SingleAsync(x => x.StockKeepingUnitId == otherSku.Id)).Quantity);
        Assert.Equal(taskId, (await verify.StorageLocationLocks.SingleAsync(x => x.StorageLocationId == sourceId)).OwnerId);
        Assert.Null((await verify.StockShortages.SingleAsync(x => x.Id == shortage.Id)).ResolvedAtUtc);
        Assert.Equal(2, await verify.InventoryTurnovers.CountAsync(x => x.InventoryMovementId == movement.Id));
    }

    [Fact]
    public async Task Shortage_correction_preserves_picked_stock_and_clears_only_its_block_atomically()
    {
        var (warehouseId, sourceId, _, skuId) = await SeedAsync(ZoneType.Storage);
        var now = DateTimeOffset.UtcNow;
        var reservation = Value(PickingReservation.Create(Guid.NewGuid(), Guid.NewGuid(), warehouseId,
            sourceId, skuId, null, 7, UserId, now));
        var shortage = Value(StockShortage.Report(Guid.NewGuid(), warehouseId, sourceId, skuId,
            null, StockShortageOrigin.Picking, reservation.PickingAssemblyId, 10, 7, null, UserId, now));
        await using (var db = database.CreateDbContext())
        {
            db.AddRange(reservation, shortage,
                Value(InventoryBalance.Create(Guid.NewGuid(), warehouseId, sourceId, skuId, 10, now)));
            await db.SaveChangesAsync();
        }
        var excessive = Value(InventoryMovement.Create(Guid.NewGuid(), warehouseId, sourceId, null,
            skuId, 4, now, RecorderType.StockCorrection, shortage.Id, 1, UserId));
        Error(await PostAsync(new(excessive, InventoryPostingKind.StockCorrection,
            now.AddSeconds(1), ShortageId: shortage.Id), Guid.NewGuid()), OperationErrorType.Conflict);
        var correction = Value(InventoryMovement.Create(Guid.NewGuid(), warehouseId, sourceId, null,
            skuId, 3, now, RecorderType.StockCorrection, shortage.Id, 1, UserId));
        var requestId = Guid.NewGuid();
        var command = new PostInventoryMovement.Command(correction, InventoryPostingKind.StockCorrection,
            now.AddSeconds(2), ShortageId: shortage.Id);
        Value(await PostAsync(command, requestId, shortage.Id));
        Value(await PostAsync(command, requestId, shortage.Id));

        await using var verify = database.CreateDbContext();
        Assert.Equal(7m, (await verify.InventoryBalances.SingleAsync(x => x.WarehouseId == warehouseId)).Quantity);
        var saved = await verify.PickingReservations.SingleAsync(x => x.PickingEntryId == reservation.PickingEntryId);
        Assert.Equal(7m, saved.Quantity);
        Assert.Null(saved.CompletedAtUtc);
        var resolved = await verify.StockShortages.SingleAsync(x => x.Id == shortage.Id);
        Assert.NotNull(resolved.ResolvedAtUtc);
        Assert.Equal(correction.Id, resolved.CorrectionMovementId);
        Assert.False(await verify.InventoryMovements.AnyAsync(x => x.Id == excessive.Id));
        Assert.Single(await verify.InventoryTurnovers.Where(x => x.InventoryMovementId == correction.Id).ToArrayAsync());
    }

    private Task<OperationResult<Guid>> PostAsync(
        PostInventoryMovement.Command command, Guid requestId, Guid? resolveShortageId = null)
    {
        var executor = new CommandExecutor(new CommandExecutionStore(database));
        var postingStore = new InventoryPostingStore();
        var posting = new PostInventoryMovement.Handler(postingStore);
        return executor.ExecuteAsync(CommandType, requestId,
            CommandExecutor.ComputeHash(command.Movement.Id.ToString()), UserId,
            async (context, ct) =>
            {
                var result = await posting.HandleAsync(command, context, ct);
                if (!result.IsSuccess)
                    return result.Error!;
                if (resolveShortageId is Guid shortageId)
                {
                    // The production Manager command resolves the tracked report in this same session.
                    var state = await postingStore.LoadAsync(context, command.Movement, ct);
                    var report = state.Shortages.Single(x => x.Id == shortageId);
                    Success(report.Resolve(UserId, command.PostedAtUtc, "Недостача подтверждена", command.Movement.Id));
                }
                return new CommandExecutionResult(command.Movement.Id,
                    [Value(WarehouseAuditEvent.Create(CommandType, command.PostedAtUtc,
                        UserId, null, null, null, command.Movement.StockKeepingUnitId, null,
                        null, null, [command.Movement.Id], commandUserId: UserId,
                        commandType: CommandType, requestId: requestId))]);
            }, default);
    }

    private async Task<(Guid WarehouseId, Guid SourceId, Guid DestinationId, Guid SkuId)> SeedAsync(ZoneType sourceType)
    {
        var warehouse = new Warehouse { Id = Guid.NewGuid(), Name = "Outbound posting" };
        var sku = new StockKeepingUnit { Id = Guid.NewGuid(), Name = "Posted SKU" };
        var sourceZone = Zone(warehouse.Id, "SRC", sourceType);
        var destinationZone = Zone(warehouse.Id, "DST", ZoneType.Storage);
        var source = Location(warehouse.Id, sourceZone, 1, allowsMultipleSkus: true);
        var destination = Location(warehouse.Id, destinationZone, 1);
        await using var db = database.CreateDbContext();
        db.AddRange(warehouse, sku, sourceZone, destinationZone, source, destination);
        await db.SaveChangesAsync();
        return (warehouse.Id, source.Id, destination.Id, sku.Id);
    }
}
