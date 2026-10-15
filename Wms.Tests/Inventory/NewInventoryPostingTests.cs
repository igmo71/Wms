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
public sealed class NewInventoryPostingTests(SqlDatabase database)
{
    [Fact]
    public async Task Two_movements_in_one_command_share_the_staged_balance()
    {
        var now = DateTimeOffset.UtcNow;
        var warehouse = new Warehouse { Id = Guid.NewGuid(), Name = "Batch posting" };
        var sku = new StockKeepingUnit { Id = Guid.NewGuid(), Name = "Batch SKU" };
        var zone = Zone(warehouse.Id, "NB", ZoneType.Receiving);
        var location = Location(warehouse.Id, zone, 1);
        var batch = Value(LicensePlateNumberBatch.Issue(1, "test-user", now));
        var lpnId = batch.Labels.Single().Id;
        await using (var seed = database.CreateDbContext())
        {
            seed.AddRange(warehouse, sku, zone, location, batch);
            await seed.SaveChangesAsync();
        }

        var orderId = Guid.NewGuid();
        var receipt = Value(InventoryMovement.Create(Guid.NewGuid(), warehouse.Id,
            null, location.Id, sku.Id, 5m, now, RecorderType.ReceivingOrder,
            orderId, 1, "test-user", licensePlateNumberId: lpnId));
        var correction = Value(InventoryMovement.Create(Guid.NewGuid(), warehouse.Id,
            null, location.Id, sku.Id, 2m, now.AddSeconds(1),
            RecorderType.ReceivingOrder, orderId, 2, "test-user",
            licensePlateNumberId: lpnId));
        var executor = new CommandExecutor(new CommandExecutionStore(database));
        var posting = new PostInventoryMovement.Handler(new InventoryPostingStore());
        var requestId = Guid.NewGuid();
        var commandType = "test.batch-posting";
        Value(await executor.ExecuteAsync(commandType, requestId,
            CommandExecutor.ComputeHash(requestId.ToString()), "test-user",
            async (context, ct) =>
            {
                var first = await posting.HandleAsync(new(receipt, InventoryPostingKind.Receipt,
                    now.AddSeconds(1)), context, ct);
                if (!first.IsSuccess)
                    return OperationResult<CommandExecutionResult>.Failure(first.Error!);
                var second = await posting.HandleAsync(new(correction, InventoryPostingKind.Correction,
                    now.AddSeconds(2)), context, ct);
                if (!second.IsSuccess)
                    return OperationResult<CommandExecutionResult>.Failure(second.Error!);
                var audit = Value(WarehouseAuditEvent.Create(commandType, now.AddSeconds(2),
                    "test-user", null, null, lpnId, sku.Id, null, null, null,
                    [receipt.Id, correction.Id], commandUserId: "test-user",
                    commandType: commandType, requestId: requestId));
                return new CommandExecutionResult(receipt.Id, [audit]);
            }, default));

        await using var verify = database.CreateDbContext();
        var balance = await verify.InventoryBalances.SingleAsync(x =>
            x.WarehouseId == warehouse.Id && x.StorageLocationId == location.Id);
        Assert.Equal(7m, balance.Quantity);
        var turnovers = await verify.InventoryTurnovers
            .Where(x => x.WarehouseId == warehouse.Id)
            .OrderBy(x => x.CreatedAtUtc).ToListAsync();
        Assert.Equal(2, turnovers.Count);
        Assert.Equal((0m, 5m, 5m), (turnovers[0].BalanceBefore,
            turnovers[0].QuantityDelta, turnovers[0].BalanceAfter));
        Assert.Equal((5m, 2m, 7m), (turnovers[1].BalanceBefore,
            turnovers[1].QuantityDelta, turnovers[1].BalanceAfter));
    }

    [Fact]
    public async Task Receipt_putaway_and_historical_reversal_keep_date_provenance_and_signed_turnover()
    {
        var now = DateTimeOffset.UtcNow;
        var date = new DateOnly(2027, 1, 15);
        var warehouse = new Warehouse { Id = Guid.NewGuid(), Name = "New posting" };
        var sku = new StockKeepingUnit { Id = Guid.NewGuid(), Name = "Dated SKU" };
        var receivingZone = Zone(warehouse.Id, "NR", ZoneType.Receiving);
        var storageZone = Zone(warehouse.Id, "NS", ZoneType.Storage);
        var receiving = Location(warehouse.Id, receivingZone, 1);
        var storage = Location(warehouse.Id, storageZone, 1);
        var batch = Value(LicensePlateNumberBatch.Issue(1, "test-user", now));
        var lpnId = batch.Labels.Single().Id;
        await using (var seed = database.CreateDbContext())
        {
            seed.AddRange(warehouse, sku, receivingZone, storageZone, receiving, storage, batch);
            await seed.SaveChangesAsync();
        }

        var orderId = Guid.NewGuid();
        var receipt = Value(InventoryMovement.Create(Guid.NewGuid(), warehouse.Id,
            null, receiving.Id, sku.Id, 5m, now, RecorderType.ReceivingOrder,
            orderId, 1, "test-user", date, lpnId));
        var putaway = Value(InventoryMovement.Create(Guid.NewGuid(), warehouse.Id,
            receiving.Id, storage.Id, sku.Id, 5m, now.AddSeconds(2),
            RecorderType.ReceivingOrder, orderId, 2, "test-user", date, lpnId));

        await Post(receipt, InventoryPostingKind.Receipt, now.AddSeconds(1));
        await Post(putaway, InventoryPostingKind.Putaway, now.AddSeconds(3));

        await using (var lockContext = database.CreateDbContext())
        {
            var location = await lockContext.StorageLocations.SingleAsync(x => x.Id == receiving.Id);
            location.AdvanceOperationalRevision();
            var zeroBalance = await lockContext.InventoryBalances.SingleAsync(x =>
                x.StorageLocationId == receiving.Id && x.StockKeepingUnitId == sku.Id);
            lockContext.InventoryBalances.Remove(zeroBalance);
            lockContext.StorageLocationLocks.Add(Value(StorageLocationLock.CreateManual(
                receiving.Id, "Historical reversal", now.AddSeconds(4), "test-user")));
            await lockContext.SaveChangesAsync();
        }

        var reversal = Value(InventoryMovement.CreateReversal(Guid.NewGuid(), receipt,
            now.AddSeconds(5), RecorderType.ReceivingOrder, orderId, 3, "test-user"));
        await Post(reversal, InventoryPostingKind.Reversal, now.AddSeconds(6));

        await using var verify = database.CreateDbContext();
        var movements = await verify.InventoryMovements
            .Where(x => x.LicensePlateNumberId == lpnId).ToListAsync();
        Assert.Equal(3, movements.Count);
        Assert.All(movements, x => Assert.Equal(date, x.ExpirationDate));
        Assert.Equal(receipt.Id, reversal.ReversedMovementId);
        var balances = await verify.InventoryBalances
            .Where(x => x.WarehouseId == warehouse.Id && x.StockKeepingUnitId == sku.Id)
            .ToDictionaryAsync(x => x.StorageLocationId);
        Assert.Equal(-5m, balances[receiving.Id].Quantity);
        Assert.Equal(5m, balances[storage.Id].Quantity);
        var lastTurnover = await verify.InventoryTurnovers.SingleAsync(x => x.InventoryMovementId == reversal.Id);
        Assert.Equal(date, lastTurnover.ExpirationDate);
        Assert.Equal(0m, lastTurnover.BalanceBefore);
        Assert.Equal(-5m, lastTurnover.QuantityDelta);
        Assert.Equal(-5m, lastTurnover.BalanceAfter);

        async Task Post(InventoryMovement movement, InventoryPostingKind kind, DateTimeOffset postedAtUtc)
        {
            var executor = new CommandExecutor(new CommandExecutionStore(database));
            var posting = new PostInventoryMovement.Handler(new InventoryPostingStore());
            var requestId = Guid.NewGuid();
            var commandType = "test.new-posting";
            var result = await executor.ExecuteAsync(commandType, requestId,
                CommandExecutor.ComputeHash(movement.Id.ToString()), "test-user",
                async (context, ct) =>
                {
                    var posted = await posting.HandleAsync(
                        new PostInventoryMovement.Command(movement, kind, postedAtUtc), context, ct);
                    if (!posted.IsSuccess)
                        return OperationResult<CommandExecutionResult>.Failure(posted.Error!);
                    var audit = Value(WarehouseAuditEvent.Create(
                        commandType, postedAtUtc, "test-user", null,
                        null, lpnId, sku.Id, null, null, null,
                        [movement.Id], commandUserId: "test-user",
                        commandType: commandType, requestId: requestId));
                    return new CommandExecutionResult(movement.Id, [audit]);
                }, default);
            Assert.Equal(movement.Id, Value(result));
        }
    }
}
