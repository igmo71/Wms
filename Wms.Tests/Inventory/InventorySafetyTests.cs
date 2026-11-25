using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Wms.Application.Authorization;
using Wms.Application.Commands;
using Wms.Application.Inventory;
using Wms.Application.Inventory.Counts;
using Wms.Application.Inventory.Movements;
using Wms.Application.Inventory.Transfers;
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
    public async Task Route_reserved_stock_limits_transfer_availability_and_posting()
    {
        var warehouse = new Warehouse { Id = Guid.NewGuid(), Name = "Reserved transfer stock" };
        var sku = new StockKeepingUnit { Id = Guid.NewGuid(), Name = "Reserved transfer SKU" };
        var zone = Zone(warehouse.Id, "TR", ZoneType.Storage);
        var source = Location(warehouse.Id, zone, 1);
        var destination = Location(warehouse.Id, zone, 2);
        var assembly = PickingAssembly(warehouse.Id, "route-picker");
        await using (var seed = database.CreateDbContext())
        {
            seed.AddRange(warehouse, sku, zone, source, destination, assembly,
                Balance(warehouse.Id, source.Id, sku.Id, 10m),
                Value(PickingRouteReservation.Create(assembly.Id, warehouse.Id, source.Id, sku.Id, null, 8m)));
            await seed.SaveChangesAsync();
        }

        var service = TransferService(database);
        var transfer = Value(await service.CreateAsync(new(warehouse.Id, null), Command("transfer-worker")));
        var query = new InventoryTransferQueryService(database);
        Assert.Equal(2m, Value(await query.GetAvailableDirectQuantityAsync(transfer, source.Id, sku.Id)));
        var match = Assert.Single(Value(await query.SearchAvailableDirectSkusAsync(transfer, source.Id, "Reserved", 10)));
        Assert.Equal(2m, match.AvailableQuantity);
        Assert.Equal(2m, Assert.Single(await query.GetStorageLocationBalancesAsync(source.Id)).Quantity);

        Error(await service.MoveDirectAsync(new(transfer, source.Id, destination.Id, sku.Id, 3m),
            Command("transfer-worker")), OperationErrorType.Conflict);
        Value(await service.MoveDirectAsync(new(transfer, source.Id, destination.Id, sku.Id, 2m),
            Command("transfer-worker")));

        await using var verify = database.CreateDbContext();
        Assert.Equal(8m, (await verify.InventoryBalances.SingleAsync(x => x.StorageLocationId == source.Id)).Quantity);
        Assert.Equal(2m, (await verify.InventoryBalances.SingleAsync(x => x.StorageLocationId == destination.Id)).Quantity);
        Assert.Single(await verify.InventoryMovements.Where(x => x.RecorderId == transfer).ToArrayAsync());
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
    public async Task Concurrent_transfers_cannot_mix_distinct_skus_in_restricted_location()
    {
        var warehouse = new Warehouse { Id = Guid.NewGuid(), Name = "Destination race" };
        var firstSku = new StockKeepingUnit { Id = Guid.NewGuid(), Name = "First SKU" };
        var secondSku = new StockKeepingUnit { Id = Guid.NewGuid(), Name = "Second SKU" };
        var zone = Zone(warehouse.Id, "RM", ZoneType.Storage);
        var firstSource = Location(warehouse.Id, zone, 1);
        var secondSource = Location(warehouse.Id, zone, 2);
        var destination = Location(warehouse.Id, zone, 3);
        await using (var seed = database.CreateDbContext())
        {
            seed.AddRange(warehouse, firstSku, secondSku, zone,
                firstSource, secondSource, destination,
                Balance(warehouse.Id, firstSource.Id, firstSku.Id, 1m),
                Balance(warehouse.Id, secondSource.Id, secondSku.Id, 1m));
            await seed.SaveChangesAsync();
        }

        var service = TransferService(database);
        var firstTransfer = Value(await service.CreateAsync(
            new(warehouse.Id, null), Command("mix-a")));
        var secondTransfer = Value(await service.CreateAsync(
            new(warehouse.Id, null), Command("mix-b")));
        var racingService = TransferService(database.WithInterceptor(new ReceiptSaveBarrier()));
        var results = await Task.WhenAll(
            racingService.MoveDirectAsync(new(firstTransfer, firstSource.Id,
                destination.Id, firstSku.Id, 1m), Command("mix-a")),
            racingService.MoveDirectAsync(new(secondTransfer, secondSource.Id,
                destination.Id, secondSku.Id, 1m), Command("mix-b")));

        Assert.Single(results, result => result.IsSuccess);
        Error(results.Single(result => !result.IsSuccess), OperationErrorType.Conflict);
        await using var verify = database.CreateDbContext();
        var destinationBalances = await verify.InventoryBalances
            .Where(x => x.StorageLocationId == destination.Id && x.Quantity != 0)
            .ToListAsync();
        Assert.Single(destinationBalances);
        Assert.Equal(1m, destinationBalances[0].Quantity);
        Assert.Equal(2m, await verify.InventoryBalances
            .Where(x => x.WarehouseId == warehouse.Id)
            .SumAsync(x => x.Quantity));
    }

    [Fact]
    public async Task Inventory_count_reports_all_blocking_stock_commitments()
    {
        var setup = await CreateCountSetup(10m);
        var assembly = PickingAssembly(setup.WarehouseId, "picker");
        var now = DateTimeOffset.UtcNow;
        var otherDate = DateOnly.FromDateTime(now.UtcDateTime).AddDays(30);
        var entry = Value(PickingEntry.Create(assembly.Id, setup.WarehouseId, setup.LocationId,
            setup.ExpectedSkuId, null, 2m, "picker", now));
        await using (var seed = database.CreateDbContext())
        {
            seed.AddRange(assembly, entry,
                Value(PickingRouteReservation.Create(assembly.Id, setup.WarehouseId,
                    setup.LocationId, setup.ExpectedSkuId, null, 3m)),
                Value(PickingReservation.Create(entry.Id, assembly.Id, setup.WarehouseId,
                    setup.LocationId, setup.ExpectedSkuId, null, 2m, "picker", now)),
                Value(InventoryBalance.Create(Guid.NewGuid(), setup.WarehouseId,
                    setup.LocationId, setup.ExpectedSkuId, 4m, now, otherDate)),
                Value(StockShortage.Report(Guid.NewGuid(), setup.WarehouseId, setup.LocationId,
                    setup.ExpectedSkuId, otherDate, assembly.Id, 4m, 0m, null, "picker", now)));
            await seed.SaveChangesAsync();
        }

        var result = await CountService(database).StartAsync(
            new(setup.WarehouseId, setup.LocationId), Command("counter"));
        Error(result, OperationErrorType.Conflict);
        var message = result.Error!.Message;
        Assert.Contains("товар закреплён за сборкой", message);
        Assert.Contains("есть резерв подтверждённого отбора", message);
        Assert.Contains("недостача ожидает решения руководителя", message);
        await using var verify = database.CreateDbContext();
        Assert.False(await verify.InventoryCounts.AnyAsync(x => x.StorageLocationId == setup.LocationId));
        Assert.False(await verify.StorageLocationLocks.AnyAsync(x => x.StorageLocationId == setup.LocationId));
    }

    [Fact]
    public async Task Inventory_count_posts_signed_dated_groups_once_and_releases_reserve_cell()
    {
        var date = new DateOnly(2025, 1, 31);
        var newDate = date.AddMonths(1);
        var setup = await CreateCountSetup(-2m, zoneType: ZoneType.Reserve, expirationDate: date);
        await using (var seed = database.CreateDbContext())
        {
            var sku = await seed.StockKeepingUnits.SingleAsync(x => x.Id == setup.ExpectedSkuId);
            sku.SetExpirationTracking(true);
            seed.Add(Balance(setup.WarehouseId, setup.LocationId, sku.Id, 5m));
            await seed.SaveChangesAsync();
        }
        var service = CountService(database);
        var countId = Value(await service.StartAsync(new(setup.WarehouseId, setup.LocationId), Command("count-correction")));
        var count = await LoadCount(countId);
        var negative = Assert.Single(count.Items, x => x.ExpirationDate == date);
        Assert.True(negative.IsExpected);
        Assert.Equal(-2m, negative.ExpectedQuantity);
        var undated = Assert.Single(count.Items, x => x.ExpirationDate is null);
        Error(await service.SetCountedQuantityAsync(new(countId, undated.Id, 2m), Command("count-correction")),
            OperationErrorType.Invalid);
        Value(await service.SetCountedQuantityAsync(new(countId, undated.Id, 2m, true), Command("count-correction")));
        Value(await service.SetCountedQuantityAsync(new(countId, negative.Id, 0m), Command("count-correction")));
        Value(await service.SetSkuCountedQuantityAsync(
            new(countId, setup.ExpectedSkuId, 3m, newDate, true), Command("count-correction")));
        var postContext = Command("count-correction");

        Value(await service.PostAsync(countId, postContext));
        Value(await service.PostAsync(countId, postContext));

        await using var verify = database.CreateDbContext();
        var balances = await verify.InventoryBalances.Where(x => x.StorageLocationId == setup.LocationId).ToListAsync();
        Assert.Equal(2m, Assert.Single(balances, x => x.ExpirationDate is null).Quantity);
        Assert.Equal(0m, Assert.Single(balances, x => x.ExpirationDate == date).Quantity);
        Assert.Equal(3m, Assert.Single(balances, x => x.ExpirationDate == newDate).Quantity);
        Assert.Equal(3, await verify.InventoryMovements.CountAsync(x => x.RecorderId == countId && x.PostedAtUtc != null));
        Assert.Equal(3, await verify.InventoryTurnovers.CountAsync(x => x.InventoryMovement!.RecorderId == countId));
        Assert.False(await verify.StorageLocationLocks.AnyAsync(x => x.StorageLocationId == setup.LocationId));
    }

    [Fact]
    public async Task Restricted_count_accepts_sku_replacement_but_rejects_mixed_final_stock()
    {
        var setup = await CreateCountSetup(10m, allowsMultipleSkus: false);
        var service = CountService(database);
        var countId = Value(await service.StartAsync(
            new(setup.WarehouseId, setup.LocationId), Command("count-replace")));
        var count = await LoadCount(countId);
        Value(await service.SetCountedQuantityAsync(
            new(countId, count.Items.Single().Id, 0m), Command("count-replace")));
        Value(await service.SetSkuCountedQuantityAsync(
            new(countId, setup.UnexpectedSkuId, 10m), Command("count-replace")));
        Value(await service.PostAsync(countId, Command("count-replace")));

        var secondId = Value(await service.StartAsync(
            new(setup.WarehouseId, setup.LocationId), Command("count-mixed")));
        var second = await LoadCount(secondId);
        Value(await service.SetCountedQuantityAsync(
            new(secondId, second.Items.Single().Id, 10m), Command("count-mixed")));
        Value(await service.SetSkuCountedQuantityAsync(
            new(secondId, setup.ExpectedSkuId, 1m), Command("count-mixed")));
        Error(await service.PostAsync(secondId, Command("count-mixed")),
            OperationErrorType.Conflict);

        await using var verify = database.CreateDbContext();
        var balances = await verify.InventoryBalances
            .Where(x => x.StorageLocationId == setup.LocationId)
            .ToDictionaryAsync(x => x.StockKeepingUnitId);
        Assert.Equal(0m, balances[setup.ExpectedSkuId].Quantity);
        Assert.Equal(10m, balances[setup.UnexpectedSkuId].Quantity);
        Assert.Equal(InventoryCountStatus.Draft,
            (await verify.InventoryCounts.SingleAsync(x => x.Id == secondId)).Status);
        Assert.False(await verify.InventoryMovements.AnyAsync(x => x.RecorderId == secondId));
        Error(await service.SetSkuMixingAsync(setup.LocationId, true, Command("count-mixed")), OperationErrorType.Conflict);
        Value(await service.SetSkuMixingAsync(setup.LocationId, true, Command("count-manager")));
        Value(await service.PostAsync(secondId, Command("count-mixed")));
        Error(await service.SetSkuMixingAsync(setup.LocationId, false, Command("count-manager")),
            OperationErrorType.Conflict);
        Assert.Equal(1m, await verify.InventoryBalances.Where(x => x.StorageLocationId == setup.LocationId
            && x.StockKeepingUnitId == setup.ExpectedSkuId).Select(x => x.Quantity).SingleAsync());
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

    [Fact]
    public async Task Concurrent_count_starts_share_one_draft_and_hold()
    {
        var setup = await CreateCountSetup(10m);
        var service = CountService(database);
        var command = new StartInventoryCountCommand(setup.WarehouseId, setup.LocationId);
        var starts = await Task.WhenAll(service.StartAsync(command, Command("counter-one")),
            service.StartAsync(command, Command("counter-two")));
        var countId = Value(starts[0]);
        Assert.Equal(countId, Value(starts[1]));

        await using var verify = database.CreateDbContext();
        Assert.Equal(1, await verify.InventoryCounts.CountAsync(x =>
            x.StorageLocationId == setup.LocationId && x.Status == InventoryCountStatus.Draft));
        Assert.Equal(countId, (await verify.StorageLocationLocks.SingleAsync(x =>
            x.StorageLocationId == setup.LocationId)).OwnerId);
    }

    private async Task<CountSetup> CreateCountSetup(decimal quantity, bool allowsMultipleSkus = false,
        ZoneType zoneType = ZoneType.Storage, DateOnly? expirationDate = null)
    {
        var warehouse = new Warehouse { Id = Guid.NewGuid(), Name = "Count safety" };
        var expectedSku = new StockKeepingUnit { Id = Guid.NewGuid(), Name = "Expected SKU" };
        var unexpectedSku = new StockKeepingUnit { Id = Guid.NewGuid(), Name = "Unexpected SKU" };
        var zone = Zone(warehouse.Id, "CS", zoneType);
        var location = Location(warehouse.Id, zone, 1, allowsMultipleSkus);
        var balance = Value(InventoryBalance.Create(Guid.NewGuid(), warehouse.Id, location.Id,
            expectedSku.Id, quantity, DateTimeOffset.UtcNow, expirationDate, allowNegative: true));
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

    private static InventoryTransferCommandService TransferService(IDbContextFactory<ApplicationDbContext> factory) =>
        new(new CommandExecutor(factory), new InventoryPostingService(NullLogger<InventoryPostingService>.Instance));

    private static InventoryCountCommandService CountService(IDbContextFactory<ApplicationDbContext> factory) =>
        new(new Wms.Application.CommandExecution.CommandExecutor(new CommandExecutionStore(factory)),
            new InventoryCountStore(), new PostInventoryMovement.Handler(new InventoryPostingStore()),
            new WarehousePermission(new CountPermission()), TimeProvider.System);

    private sealed class CountPermission : IWarehousePermissionStore
    {
        public Task<bool> HasManagerRoleAsync(string userId, CancellationToken ct) =>
            Task.FromResult(userId == "count-manager");

        public Task<bool> HasOperatorRoleAsync(string userId, CancellationToken ct) =>
            Task.FromResult(userId != "count-manager");
    }

    private sealed record CountSetup(Guid WarehouseId, Guid LocationId, Guid ExpectedSkuId, Guid UnexpectedSkuId, Guid ExpectedBalanceId);
}
