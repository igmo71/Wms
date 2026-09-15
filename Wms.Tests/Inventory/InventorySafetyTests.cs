using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Wms.Application.Commands;
using Wms.Application.Inventory.Counts;
using Wms.Application.Inventory.Movements;
using Wms.Application.Inventory.Transfers;
using Wms.Application.ReceivingOrders;
using Wms.Application.StockKeepingUnits;
using Wms.Common;
using Wms.Data;
using Wms.Domain;
using Wms.Domain.Enums;
using Wms.Tests.Infrastructure;
using static Wms.Tests.Infrastructure.TestSupport;

namespace Wms.Tests.Inventory;

[Collection(SqlDatabaseCollection.Name)]
public sealed class InventorySafetyTests(SqlDatabase database)
{
    [Fact]
    public async Task Incomplete_putaway_remains_a_draft_and_does_not_change_stock()
    {
        var setup = await CreatePutawaySetup();
        var service = PutawayService(database);
        Value(await service.StartAsync(setup.OrderId, Command("putaway-incomplete")));
        Value(await service.AddMovementAsync(new(setup.OrderId, 1, setup.FirstDestinationId, 4m), Command("putaway-incomplete")));

        var completion = await service.CompleteAsync(setup.OrderId, Command("putaway-incomplete"));

        Error(completion, OperationErrorType.Invalid);
        await using var verify = database.CreateDbContext();
        Assert.Equal(PutawayStatus.InProgress, (await verify.ReceivingOrders.SingleAsync(x => x.Id == setup.OrderId)).PutawayStatus);
        Assert.False(await verify.InventoryMovements.AnyAsync(x => x.RecorderId == setup.OrderId && x.PostedAtUtc != null));
        Assert.False(await verify.InventoryTurnovers.AnyAsync(x => x.InventoryMovement!.RecorderId == setup.OrderId));
        Assert.Equal(100m, (await verify.InventoryBalances.SingleAsync(x => x.Id == setup.SourceBalanceId)).Quantity);
    }

    [Fact]
    public async Task Putaway_completion_is_atomic_and_replay_does_not_post_twice()
    {
        var setup = await CreatePutawaySetup();
        var service = PutawayService(database);
        Value(await service.StartAsync(setup.OrderId, Command("putaway-complete")));
        Value(await service.AddMovementAsync(new(setup.OrderId, 1, setup.FirstDestinationId, 4m), Command("putaway-complete")));
        Value(await service.AddMovementAsync(new(setup.OrderId, 1, setup.SecondDestinationId, 6m), Command("putaway-complete")));

        await using (var lockContext = database.CreateDbContext())
        {
            lockContext.StorageLocationLocks.Add(Value(StorageLocationLock.CreateManual(
                setup.SecondDestinationId, "Completion barrier", DateTimeOffset.UtcNow, "putaway-complete")));
            await lockContext.SaveChangesAsync();
        }

        var completionContext = Command("putaway-complete");
        Assert.False((await service.CompleteAsync(setup.OrderId, completionContext)).IsSuccess);
        await AssertPutawayUnposted(setup);

        await using (var unlockContext = database.CreateDbContext())
        {
            unlockContext.StorageLocationLocks.Remove(await unlockContext.StorageLocationLocks.SingleAsync(x => x.StorageLocationId == setup.SecondDestinationId));
            await unlockContext.SaveChangesAsync();
        }

        Value(await service.CompleteAsync(setup.OrderId, completionContext));
        Value(await service.CompleteAsync(setup.OrderId, completionContext));

        await using var verify = database.CreateDbContext();
        Assert.Equal(PutawayStatus.Completed, (await verify.ReceivingOrders.SingleAsync(x => x.Id == setup.OrderId)).PutawayStatus);
        Assert.Equal(2, await verify.InventoryMovements.CountAsync(x => x.RecorderId == setup.OrderId && x.PostedAtUtc != null));
        Assert.Equal(4, await verify.InventoryTurnovers.CountAsync(x => x.InventoryMovement!.RecorderId == setup.OrderId));
        Assert.Equal(90m, (await verify.InventoryBalances.SingleAsync(x => x.Id == setup.SourceBalanceId)).Quantity);
        Assert.Equal(4m, (await verify.InventoryBalances.SingleAsync(x => x.StorageLocationId == setup.FirstDestinationId)).Quantity);
        Assert.Equal(6m, (await verify.InventoryBalances.SingleAsync(x => x.StorageLocationId == setup.SecondDestinationId)).Quantity);
    }

    [Fact]
    public async Task Concurrent_transfers_cannot_withdraw_the_same_stock_twice()
    {
        var warehouse = new Warehouse { Id = Guid.NewGuid(), Name = "Transfer race" };
        var sku = new StockKeepingUnit { Id = Guid.NewGuid(), Name = "Transfer SKU" };
        var zone = Zone(warehouse.Id, "TR", ZoneType.Storage);
        var source = Location(warehouse.Id, zone, 1);
        var firstDestination = Location(warehouse.Id, zone, 2);
        var secondDestination = Location(warehouse.Id, zone, 3);
        await using (var seed = database.CreateDbContext())
        {
            seed.AddRange(warehouse, sku, zone, source, firstDestination, secondDestination,
                Balance(warehouse.Id, source.Id, sku.Id, 1m));
            await seed.SaveChangesAsync();
        }

        var service = TransferService(database);
        var firstTransfer = Value(await service.CreateAsync(new(warehouse.Id, null), Command("transfer-a")));
        var secondTransfer = Value(await service.CreateAsync(new(warehouse.Id, null), Command("transfer-b")));
        var racingService = TransferService(database.WithInterceptor(new ReceiptSaveBarrier()));

        var results = await Task.WhenAll(
            racingService.MoveDirectAsync(new(firstTransfer, source.Id, firstDestination.Id, sku.Id, 1m), Command("transfer-a")),
            racingService.MoveDirectAsync(new(secondTransfer, source.Id, secondDestination.Id, sku.Id, 1m), Command("transfer-b")));

        Assert.Single(results, result => result.IsSuccess);
        Error(results.Single(result => !result.IsSuccess), OperationErrorType.Conflict);
        await using var verify = database.CreateDbContext();
        var balances = await verify.InventoryBalances.Where(x => x.WarehouseId == warehouse.Id).ToListAsync();
        Assert.DoesNotContain(balances, balance => balance.Quantity < 0);
        Assert.Equal(1m, balances.Sum(balance => balance.Quantity));
        Assert.Equal(1, await verify.InventoryMovements.CountAsync(x =>
            (x.RecorderId == firstTransfer || x.RecorderId == secondTransfer) && x.PostedAtUtc != null));
    }

    [Fact]
    public async Task Inventory_count_posts_positive_and_negative_corrections_once_and_releases_lock()
    {
        var setup = await CreateCountSetup(10m);
        var service = CountService(database);
        var countId = Value(await service.StartAsync(new(setup.WarehouseId, setup.LocationId), Command("count-correction")));
        var count = await LoadCount(countId);
        Value(await service.SetCountedQuantityAsync(new(countId, count.Items.Single().Id, 8.5m), Command("count-correction")));
        Value(await service.SetSkuCountedQuantityAsync(new(countId, setup.UnexpectedSkuId, 2.25m), Command("count-correction")));
        var postContext = Command("count-correction");

        Value(await service.PostAsync(countId, postContext));
        Value(await service.PostAsync(countId, postContext));

        await using var verify = database.CreateDbContext();
        Assert.Equal(8.5m, (await verify.InventoryBalances.SingleAsync(x => x.StorageLocationId == setup.LocationId && x.StockKeepingUnitId == setup.ExpectedSkuId)).Quantity);
        Assert.Equal(2.25m, (await verify.InventoryBalances.SingleAsync(x => x.StorageLocationId == setup.LocationId && x.StockKeepingUnitId == setup.UnexpectedSkuId)).Quantity);
        Assert.Equal(2, await verify.InventoryMovements.CountAsync(x => x.RecorderId == countId && x.PostedAtUtc != null));
        Assert.Equal(2, await verify.InventoryTurnovers.CountAsync(x => x.InventoryMovement!.RecorderId == countId));
        Assert.False(await verify.StorageLocationLocks.AnyAsync(x => x.StorageLocationId == setup.LocationId));
    }

    [Fact]
    public async Task Inventory_count_rejects_stock_drift_without_partial_posting_and_can_retry()
    {
        var setup = await CreateCountSetup(10m);
        var service = CountService(database);
        var countId = Value(await service.StartAsync(new(setup.WarehouseId, setup.LocationId), Command("count-drift")));
        var item = (await LoadCount(countId)).Items.Single();
        Value(await service.SetCountedQuantityAsync(new(countId, item.Id, 8m), Command("count-drift")));

        await AdjustBalance(setup.ExpectedBalanceId, 1m);
        var postContext = Command("count-drift");
        Error(await service.PostAsync(countId, postContext), OperationErrorType.Conflict);

        await using (var verifyFailure = database.CreateDbContext())
        {
            Assert.Equal(InventoryCountStatus.Draft, (await verifyFailure.InventoryCounts.SingleAsync(x => x.Id == countId)).Status);
            Assert.False(await verifyFailure.InventoryMovements.AnyAsync(x => x.RecorderId == countId));
            Assert.True(await verifyFailure.StorageLocationLocks.AnyAsync(x => x.StorageLocationId == setup.LocationId));
            Assert.False(await verifyFailure.CommandReceipts.AnyAsync(x => x.RequestId == postContext.RequestId));
        }

        await AdjustBalance(setup.ExpectedBalanceId, -1m);
        Value(await service.PostAsync(countId, postContext));

        await using var verify = database.CreateDbContext();
        Assert.Equal(8m, (await verify.InventoryBalances.SingleAsync(x => x.Id == setup.ExpectedBalanceId)).Quantity);
        Assert.Equal(1, await verify.InventoryMovements.CountAsync(x => x.RecorderId == countId));
        Assert.Equal(1, await verify.InventoryTurnovers.CountAsync(x => x.InventoryMovement!.RecorderId == countId));
    }

    private async Task<PutawaySetup> CreatePutawaySetup()
    {
        var warehouse = new Warehouse { Id = Guid.NewGuid(), Name = "Putaway safety" };
        var sku = new StockKeepingUnit { Id = Guid.NewGuid(), Name = "Putaway SKU" };
        var receivingZone = Zone(warehouse.Id, "PR", ZoneType.Receiving);
        var storageZone = Zone(warehouse.Id, "PS", ZoneType.Storage);
        var source = Location(warehouse.Id, receivingZone, 1);
        var first = Location(warehouse.Id, storageZone, 1);
        var second = Location(warehouse.Id, storageZone, 2);
        var balance = Balance(warehouse.Id, source.Id, sku.Id, 100m);
        var snapshot = new ReceivingOrderImportSnapshot(
            Guid.NewGuid(), false, true, "Putaway", DateTime.UtcNow, warehouse.Id, null,
            ReceivingOrderStatus.ReadyForReceiving, default, WarehouseOperation.VendorReceipt,
            BusinessOperation.VendorPurchase, Guid.NewGuid(), default, Guid.NewGuid(), null,
            [new(1, sku.Id, 10m, 10m, null)]);
        var order = Value(ReceivingOrder.Create(snapshot, DateTimeOffset.UtcNow));
        Success(order.SetReceivingLocation(source.Id));
        Success(order.SetInReceiving(DateTimeOffset.UtcNow, "putaway"));
        Success(order.UpdateItemFactQuantity(1, 10m));
        Success(order.SetReceived(DateTimeOffset.UtcNow, "putaway"));

        await using var seed = database.CreateDbContext();
        seed.AddRange(warehouse, sku, receivingZone, storageZone, source, first, second, balance, order);
        await seed.SaveChangesAsync();
        return new(order.Id, balance.Id, first.Id, second.Id);
    }

    private async Task AssertPutawayUnposted(PutawaySetup setup)
    {
        await using var verify = database.CreateDbContext();
        Assert.False(await verify.InventoryMovements.AnyAsync(x => x.RecorderId == setup.OrderId && x.PostedAtUtc != null));
        Assert.False(await verify.InventoryTurnovers.AnyAsync(x => x.InventoryMovement!.RecorderId == setup.OrderId));
        Assert.Equal(100m, (await verify.InventoryBalances.SingleAsync(x => x.Id == setup.SourceBalanceId)).Quantity);
    }

    private async Task<CountSetup> CreateCountSetup(decimal quantity)
    {
        var warehouse = new Warehouse { Id = Guid.NewGuid(), Name = "Count safety" };
        var expectedSku = new StockKeepingUnit { Id = Guid.NewGuid(), Name = "Expected SKU" };
        var unexpectedSku = new StockKeepingUnit { Id = Guid.NewGuid(), Name = "Unexpected SKU" };
        var zone = Zone(warehouse.Id, "CS", ZoneType.Storage);
        var location = Location(warehouse.Id, zone, 1);
        var balance = Balance(warehouse.Id, location.Id, expectedSku.Id, quantity);
        await using var seed = database.CreateDbContext();
        seed.AddRange(warehouse, expectedSku, unexpectedSku, zone, location, balance);
        await seed.SaveChangesAsync();
        return new(warehouse.Id, location.Id, expectedSku.Id, unexpectedSku.Id, balance.Id);
    }

    private async Task<InventoryCount> LoadCount(Guid id)
    {
        await using var db = database.CreateDbContext();
        return await db.InventoryCounts.Include(x => x.Items).SingleAsync(x => x.Id == id);
    }

    private async Task AdjustBalance(Guid balanceId, decimal delta)
    {
        await using var db = database.CreateDbContext();
        Success((await db.InventoryBalances.SingleAsync(x => x.Id == balanceId)).Adjust(delta, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
    }

    private static PutawayCommandService PutawayService(IDbContextFactory<ApplicationDbContext> factory) =>
        new(new CommandExecutor(factory), new InventoryPostingService(NullLogger<InventoryPostingService>.Instance), NullLogger<PutawayCommandService>.Instance);

    private static InventoryTransferCommandService TransferService(IDbContextFactory<ApplicationDbContext> factory) =>
        new(new CommandExecutor(factory), new InventoryPostingService(NullLogger<InventoryPostingService>.Instance));

    private static InventoryCountCommandService CountService(IDbContextFactory<ApplicationDbContext> factory) =>
        new(new CommandExecutor(factory), new InventoryPostingService(NullLogger<InventoryPostingService>.Instance), new StockKeepingUnitService(factory));

    private sealed record PutawaySetup(Guid OrderId, Guid SourceBalanceId, Guid FirstDestinationId, Guid SecondDestinationId);
    private sealed record CountSetup(Guid WarehouseId, Guid LocationId, Guid ExpectedSkuId, Guid UnexpectedSkuId, Guid ExpectedBalanceId);
}
