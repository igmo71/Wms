using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Wms.Application.Authorization;
using Wms.Application.Commands;
using Wms.Application.LicensePlateNumbers;
using Wms.Application.Receiving;
using Wms.Application.Inventory;
using NewCommandExecutor = Wms.Application.CommandExecution.CommandExecutor;
using NewCommandExecutionResult = Wms.Application.CommandExecution.CommandExecutionResult;
using NewPostInventoryMovement = Wms.Application.Inventory.PostInventoryMovement;
using Wms.Common;
using Wms.Data;
using Wms.Domain;
using Wms.Domain.Enums;
using Wms.Tests.Infrastructure;
using static Wms.Tests.Infrastructure.TestSupport;

namespace Wms.Tests.Receiving;

[Collection(SqlDatabaseCollection.Name)]
public sealed class ReceivingSafetyTests(SqlDatabase database)
{
    [Fact]
    public async Task ReceivingLpnDeletion_reverses_own_dated_history_once_and_keeps_independent_stock_change()
    {
        var date = new DateOnly(2027, 1, 10);
        var state = await SeedNewOpenReceivingLpnAsync(date);
        var storageZone = Zone(state.WarehouseId, "RD", ZoneType.Storage);
        var first = Location(state.WarehouseId, storageZone, 2);
        var second = Location(state.WarehouseId, storageZone, 3);
        await using (var seed = database.CreateDbContext())
        {
            seed.AddRange(storageZone, first, second);
            await seed.SaveChangesAsync();
        }

        Value(await CloseThroughFoundationAsync(database, state, Guid.NewGuid()));
        var permission = new WarehousePermission(new DeletionPermission());
        var executor = new NewCommandExecutor(new CommandExecutionStore(database));
        var posting = new NewPostInventoryMovement.Handler(new InventoryPostingStore());
        var correction = new CorrectReceivingLpnQuantity(
            executor, new ReceivingWorkStore(database), posting, permission, TimeProvider.System);
        Value(await correction.ExecuteAsync(new CorrectReceivingLpnQuantityCommand(
            Guid.NewGuid(), state.OrderId, state.LpnId, 4m, "close-test")));

        Guid itemId;
        await using (var lookup = database.CreateDbContext())
            itemId = await lookup.LicensePlateNumberItems
                .Where(x => x.LicensePlateNumberId == state.LpnId)
                .Select(x => x.Id).SingleAsync();
        var putaway = new PutAwayReceivingLpn(
            executor, new ReceivingWorkStore(database), posting, permission, TimeProvider.System);
        foreach (var destination in new[] { first, second })
            Value(await putaway.ExecuteAsync(new PutAwayReceivingLpnCommand(
                Guid.NewGuid(), state.LpnId, itemId, destination.Id, 1m, "close-test")));

        // A separate inventory operation consumes the first placed unit.
        var independentId = await PostIndependentCountAsync(state, first.Id, date);
        var store = new ReceivingLpnDeletionStore(database);
        var preview = Value(await new PreviewReceivingLpnDeletion(store, permission)
            .ExecuteAsync(state.OrderId, state.LpnId, "close-test"));
        Assert.Equal(4, preview.Reversals.Count);
        Assert.True(preview.RequiresNegativeConfirmation);
        Assert.Equal(-1m, Assert.Single(preview.Balances, x =>
            x.StorageLocationId == first.Id).After);
        Assert.Equal(0m, Assert.Single(preview.Balances, x =>
            x.StorageLocationId == state.LocationId).After);

        var delete = new DeleteReceivingLpn(executor, store, posting, permission, TimeProvider.System);
        Error(await delete.ExecuteAsync(new DeleteReceivingLpnCommand(
            Guid.NewGuid(), state.OrderId, state.LpnId, preview.SnapshotToken,
            false, "close-test")), OperationErrorType.Conflict);
        var command = new DeleteReceivingLpnCommand(
            Guid.NewGuid(), state.OrderId, state.LpnId, preview.SnapshotToken,
            true, "close-test");
        Assert.Equal(state.LpnId, Value(await delete.ExecuteAsync(command)));
        Assert.Equal(state.LpnId, Value(await delete.ExecuteAsync(command)));

        await using var verify = database.CreateDbContext();
        var movements = await verify.InventoryMovements
            .Where(x => x.LicensePlateNumberId == state.LpnId).ToListAsync();
        var originals = movements.Where(x => x.ReversedMovementId is null)
            .ToDictionary(x => x.Id);
        var reversals = movements.Where(x => x.ReversedMovementId is not null).ToArray();
        Assert.Equal(4, originals.Count);
        Assert.Equal(4, reversals.Length);
        Assert.True(originals.Keys.ToHashSet().SetEquals(
            reversals.Select(x => x.ReversedMovementId!.Value)));
        foreach (var reversal in reversals)
        {
            var original = originals[reversal.ReversedMovementId!.Value];
            Assert.Equal(original.SourceStorageLocationId, reversal.DestinationStorageLocationId);
            Assert.Equal(original.DestinationStorageLocationId, reversal.SourceStorageLocationId);
            Assert.Equal(original.Quantity, reversal.Quantity);
            Assert.Equal(date, reversal.ExpirationDate);
        }
        var reversalIds = reversals.Select(x => x.Id).ToArray();
        Assert.Equal(6, await verify.InventoryTurnovers.CountAsync(x =>
            reversalIds.Contains(x.InventoryMovementId)));
        Assert.Equal(1, await verify.InventoryMovements.CountAsync(x => x.Id == independentId));
        Assert.Equal(-1m, (await verify.InventoryBalances.SingleAsync(x =>
            x.StorageLocationId == first.Id && x.StockKeepingUnitId == state.SkuId
            && x.ExpirationDate == date)).Quantity);
        Assert.Equal(LicensePlateNumberStatus.Deleted,
            (await verify.LicensePlateNumbers.SingleAsync(x => x.Id == state.LpnId)).Status);
        Assert.Null((await verify.ReceivingOrderItems.SingleAsync(x =>
            x.ReceivingOrderId == state.OrderId && x.StockKeepingUnitId == state.SkuId)).FactQuantity);
        Assert.Equal(1, await verify.WarehouseAuditEvents.CountAsync(x =>
            x.CommandType == "receiving.lpn.delete" && x.RequestId == command.RequestId));
        Assert.Equal(1, await verify.CommandReceipts.CountAsync(x =>
            x.CommandType == "receiving.lpn.delete" && x.RequestId == command.RequestId));
    }

    [Fact]
    public async Task ReceivingLpnDeletion_rejects_stale_preview_after_putaway_or_balance_change()
    {
        var state = await SeedNewOpenReceivingLpnAsync();
        var storageZone = Zone(state.WarehouseId, "RS", ZoneType.Storage);
        var destination = Location(state.WarehouseId, storageZone, 2);
        await using (var seed = database.CreateDbContext())
        {
            seed.AddRange(storageZone, destination);
            await seed.SaveChangesAsync();
        }
        Value(await CloseThroughFoundationAsync(database, state, Guid.NewGuid()));
        var permission = new WarehousePermission(new DeletionPermission());
        var store = new ReceivingLpnDeletionStore(database);
        var previewUseCase = new PreviewReceivingLpnDeletion(store, permission);
        var beforePutaway = Value(await previewUseCase.ExecuteAsync(
            state.OrderId, state.LpnId, "close-test"));
        var executor = new NewCommandExecutor(new CommandExecutionStore(database));
        var posting = new NewPostInventoryMovement.Handler(new InventoryPostingStore());
        Guid itemId;
        await using (var lookup = database.CreateDbContext())
            itemId = await lookup.LicensePlateNumberItems
                .Where(x => x.LicensePlateNumberId == state.LpnId)
                .Select(x => x.Id).SingleAsync();
        Value(await new PutAwayReceivingLpn(executor, new ReceivingWorkStore(database),
            posting, permission, TimeProvider.System).ExecuteAsync(new PutAwayReceivingLpnCommand(
                Guid.NewGuid(), state.LpnId, itemId, destination.Id, 1m, "close-test")));

        var delete = new DeleteReceivingLpn(executor, store, posting, permission, TimeProvider.System);
        Error(await delete.ExecuteAsync(new DeleteReceivingLpnCommand(
            Guid.NewGuid(), state.OrderId, state.LpnId, beforePutaway.SnapshotToken,
            true, "close-test")), OperationErrorType.Conflict);
        var beforeBalanceChange = Value(await previewUseCase.ExecuteAsync(
            state.OrderId, state.LpnId, "close-test"));
        await PostIndependentCountAsync(state, state.LocationId, null);
        var requestId = Guid.NewGuid();
        Error(await delete.ExecuteAsync(new DeleteReceivingLpnCommand(
            requestId, state.OrderId, state.LpnId, beforeBalanceChange.SnapshotToken,
            true, "close-test")), OperationErrorType.Conflict);

        await using var verify = database.CreateDbContext();
        Assert.Equal(LicensePlateNumberStatus.Closed,
            (await verify.LicensePlateNumbers.SingleAsync(x => x.Id == state.LpnId)).Status);
        Assert.False(await verify.InventoryMovements.AnyAsync(x =>
            x.LicensePlateNumberId == state.LpnId && x.ReversedMovementId != null));
        Assert.False(await verify.CommandReceipts.AnyAsync(x =>
            x.CommandType == "receiving.lpn.delete" && x.RequestId == requestId));
        Assert.False(await verify.WarehouseAuditEvents.AnyAsync(x =>
            x.CommandType == "receiving.lpn.delete" && x.RequestId == requestId));
    }

    [Fact]
    public async Task New_putaway_posts_partial_lpn_before_order_completion_once_and_rejects_overspend()
    {
        var state = await SeedNewOpenReceivingLpnAsync();
        var storageZone = Zone(state.WarehouseId, "PS", ZoneType.Storage);
        var destination = Location(state.WarehouseId, storageZone, 2);
        await using (var seed = database.CreateDbContext())
        {
            seed.AddRange(storageZone, destination);
            await seed.SaveChangesAsync();
        }
        Value(await CloseThroughFoundationAsync(database, state, Guid.NewGuid()));
        Guid itemId;
        await using (var lookup = database.CreateDbContext())
            itemId = await lookup.LicensePlateNumberItems
                .Where(x => x.LicensePlateNumberId == state.LpnId)
                .Select(x => x.Id).SingleAsync();

        var putaway = new PutAwayReceivingLpn(
            new NewCommandExecutor(new CommandExecutionStore(database)),
            new ReceivingWorkStore(database),
            new NewPostInventoryMovement.Handler(new InventoryPostingStore()),
            new WarehousePermission(new PutawayPermission()), TimeProvider.System);
        var request = new PutAwayReceivingLpnCommand(
            Guid.NewGuid(), state.LpnId, itemId, destination.Id, 2m, "close-test");
        var putawayId = Value(await putaway.ExecuteAsync(request));
        Assert.Equal(putawayId, Value(await putaway.ExecuteAsync(request)));
        Error(await putaway.ExecuteAsync(new PutAwayReceivingLpnCommand(
            Guid.NewGuid(), state.LpnId, itemId, destination.Id, 2m, "close-test")),
            OperationErrorType.Conflict);

        await using var verify = database.CreateDbContext();
        Assert.Equal(ReceivingOrderStatus.InReceiving,
            (await verify.ReceivingOrders.SingleAsync(x => x.Id == state.OrderId)).Status);
        var placed = await verify.LicensePlateNumberPutaways.SingleAsync(x => x.Id == putawayId);
        Assert.Equal(2m, placed.Quantity);
        var movement = await verify.InventoryMovements.SingleAsync(x =>
            x.Id == placed.InventoryMovementId);
        Assert.Equal(state.LpnId, movement.LicensePlateNumberId);
        Assert.Equal(state.LocationId, movement.SourceStorageLocationId);
        Assert.Equal(destination.Id, movement.DestinationStorageLocationId);
        Assert.Equal(2m, movement.Quantity);
        Assert.Equal(1m, (await verify.InventoryBalances.SingleAsync(x =>
            x.StorageLocationId == state.LocationId && x.StockKeepingUnitId == state.SkuId)).Quantity);
        Assert.Equal(2m, (await verify.InventoryBalances.SingleAsync(x =>
            x.StorageLocationId == destination.Id && x.StockKeepingUnitId == state.SkuId)).Quantity);
        Assert.Equal(2, await verify.InventoryTurnovers.CountAsync(x =>
            x.InventoryMovementId == movement.Id));
        var audit = await verify.WarehouseAuditEvents.SingleAsync(x =>
            x.CommandType == "receiving.lpn.putaway" && x.RequestId == request.RequestId);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(new[] { movement.Id }),
            audit.MovementIdsJson);
        Assert.Equal(1, await verify.CommandReceipts.CountAsync(x =>
            x.CommandType == "receiving.lpn.putaway" && x.RequestId == request.RequestId));
    }

    [Fact]
    public async Task Receiving_dates_remain_separate_at_one_destination_and_null_has_one_sql_key()
    {
        var state = await SeedNewOpenReceivingLpnAsync();
        var dates = new[] { new DateOnly(2027, 1, 10), new DateOnly(2027, 2, 10) };
        var batch = Value(LicensePlateNumberBatch.Issue(2, "close-test", DateTimeOffset.UtcNow));
        var datedLpnIds = batch.Labels.Select(x => x.Id).ToArray();
        var storageZone = Zone(state.WarehouseId, "DT", ZoneType.Storage);
        var destination = Location(state.WarehouseId, storageZone, 2);
        await using (var seed = database.CreateDbContext())
        {
            seed.AddRange(batch, storageZone, destination);
            await seed.SaveChangesAsync();
        }

        await using (var prepare = database.CreateDbContext())
        {
            var order = await prepare.ReceivingOrders
                .Include(x => x.Cycles).Include(x => x.Participants)
                .Include(x => x.Items).Include(x => x.LicensePlateNumbers)
                .ThenInclude(x => x.Items).SingleAsync(x => x.Id == state.OrderId);
            for (var index = 0; index < dates.Length; index++)
            {
                var lpn = await prepare.LicensePlateNumbers.Include(x => x.Items)
                    .SingleAsync(x => x.Id == datedLpnIds[index]);
                Assert.True(Value(order.OpenReceivingLpn(lpn, "close-test", DateTimeOffset.UtcNow)));
                Success(order.EditReceivingLpn(lpn, state.SkuId, dates[index], 1m,
                    "close-test", DateTimeOffset.UtcNow, false));
                prepare.LicensePlateNumberItems.Add(Assert.Single(lpn.Items));
            }
            await prepare.SaveChangesAsync();
        }

        Guid[] lpnIds = [state.LpnId, .. datedLpnIds];
        foreach (var lpnId in lpnIds)
            Value(await CloseThroughFoundationAsync(database, state with { LpnId = lpnId }, Guid.NewGuid()));

        var putaway = new PutAwayReceivingLpn(
            new NewCommandExecutor(new CommandExecutionStore(database)),
            new ReceivingWorkStore(database),
            new NewPostInventoryMovement.Handler(new InventoryPostingStore()),
            new WarehousePermission(new PutawayPermission()), TimeProvider.System);
        var undatedItemId = Guid.Empty;
        foreach (var lpnId in lpnIds)
        {
            Guid itemId;
            await using (var lookup = database.CreateDbContext())
                itemId = await lookup.LicensePlateNumberItems
                    .Where(x => x.LicensePlateNumberId == lpnId)
                    .Select(x => x.Id).SingleAsync();
            if (lpnId == state.LpnId) undatedItemId = itemId;
            Value(await putaway.ExecuteAsync(new PutAwayReceivingLpnCommand(
                Guid.NewGuid(), lpnId, itemId, destination.Id, 1m, "close-test")));
        }
        Value(await putaway.ExecuteAsync(new PutAwayReceivingLpnCommand(
            Guid.NewGuid(), state.LpnId, undatedItemId, destination.Id, 1m, "close-test")));

        await using (var verify = database.CreateDbContext())
        {
            var balances = await verify.InventoryBalances.Where(x =>
                x.WarehouseId == state.WarehouseId
                && x.StorageLocationId == destination.Id
                && x.StockKeepingUnitId == state.SkuId).ToListAsync();
            Assert.Equal(3, balances.Count);
            Assert.Equal(2m, Assert.Single(balances, x => x.ExpirationDate is null).Quantity);
            foreach (var date in dates)
                Assert.Equal(1m, Assert.Single(balances, x => x.ExpirationDate == date).Quantity);

            for (var index = 0; index < dates.Length; index++)
            {
                var movements = await verify.InventoryMovements.Where(x =>
                    x.LicensePlateNumberId == datedLpnIds[index]).ToListAsync();
                Assert.Equal(2, movements.Count);
                Assert.All(movements, x => Assert.Equal(dates[index], x.ExpirationDate));
                var movementIds = movements.Select(x => x.Id).ToArray();
                var turnovers = await verify.InventoryTurnovers.Where(x =>
                    movementIds.Contains(x.InventoryMovementId)).ToListAsync();
                Assert.Equal(3, turnovers.Count);
                Assert.All(turnovers, x => Assert.Equal(dates[index], x.ExpirationDate));
            }
        }

        await using var duplicate = database.CreateDbContext();
        duplicate.InventoryBalances.Add(Value(InventoryBalance.Create(Guid.NewGuid(),
            state.WarehouseId, destination.Id, state.SkuId, 1m,
            DateTimeOffset.UtcNow, null)));
        await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
    }

    [Fact]
    public async Task New_receiving_close_replay_commits_one_complete_stock_and_audit_set()
    {
        var state = await SeedNewOpenReceivingLpnAsync();
        var requestId = Guid.NewGuid();
        var first = await CloseThroughFoundationAsync(database, state, requestId);
        Assert.Equal(state.LpnId, Value(first));
        Assert.Equal(state.LpnId, Value(await CloseThroughFoundationAsync(
            database, state, requestId)));
        Error(await CloseThroughFoundationAsync(database, state, Guid.NewGuid()),
            OperationErrorType.Invalid);

        await using var verify = database.CreateDbContext();
        Assert.Equal(LicensePlateNumberStatus.Closed,
            (await verify.LicensePlateNumbers.SingleAsync(x => x.Id == state.LpnId)).Status);
        var movement = await verify.InventoryMovements.SingleAsync(x =>
            x.LicensePlateNumberId == state.LpnId);
        Assert.Equal(state.OrderId, movement.RecorderId);
        Assert.Equal(3m, movement.Quantity);
        Assert.Equal(3m, (await verify.InventoryBalances.SingleAsync(x =>
            x.StorageLocationId == state.LocationId && x.StockKeepingUnitId == state.SkuId)).Quantity);
        Assert.Equal(1, await verify.InventoryTurnovers.CountAsync(x =>
            x.InventoryMovementId == movement.Id));
        var audit = await verify.WarehouseAuditEvents.SingleAsync(x =>
            x.CommandType == "test.receiving-close" && x.LicensePlateNumberId == state.LpnId);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(new[] { movement.Id }),
            audit.MovementIdsJson);
        Assert.Equal(1, await verify.CommandReceipts.CountAsync(x =>
            x.CommandType == "test.receiving-close" && x.RequestId == requestId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task New_receiving_close_conflicts_when_plan_or_expiration_flag_changes_before_commit(
        bool changeExpirationFlag)
    {
        var state = await SeedNewOpenReceivingLpnAsync();
        var requestId = Guid.NewGuid();
        var barrier = new CompletionReceiptBarrier();
        var pending = CloseThroughFoundationAsync(
            database.WithInterceptor(barrier), state, requestId);
        await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
        try
        {
            await using var change = database.CreateDbContext();
            if (changeExpirationFlag)
            {
                var sku = await change.StockKeepingUnits.SingleAsync(x => x.Id == state.SkuId);
                sku.SetExpirationTracking(true);
            }
            else
            {
                var item = await change.ReceivingOrderItems.SingleAsync(x =>
                    x.ReceivingOrderId == state.OrderId && x.StockKeepingUnitId == state.SkuId);
                Success(item.SetCurrentPlan(2m));
            }
            await change.SaveChangesAsync();
        }
        finally
        {
            barrier.Release.TrySetResult();
        }

        Error(await pending, OperationErrorType.Conflict);
        await using var verify = database.CreateDbContext();
        Assert.Equal(LicensePlateNumberStatus.Open,
            (await verify.LicensePlateNumbers.SingleAsync(x => x.Id == state.LpnId)).Status);
        Assert.False(await verify.InventoryMovements.AnyAsync(x => x.LicensePlateNumberId == state.LpnId));
        Assert.False(await verify.InventoryBalances.AnyAsync(x =>
            x.StorageLocationId == state.LocationId && x.StockKeepingUnitId == state.SkuId));
        Assert.False(await verify.InventoryTurnovers.AnyAsync(x =>
            x.WarehouseId == state.WarehouseId && x.StockKeepingUnitId == state.SkuId));
        Assert.False(await verify.WarehouseAuditEvents.AnyAsync(x =>
            x.CommandType == "test.receiving-close" && x.LicensePlateNumberId == state.LpnId));
        Assert.False(await verify.CommandReceipts.AnyAsync(x =>
            x.CommandType == "test.receiving-close" && x.RequestId == requestId));
    }

    [Fact]
    public async Task Maintenance_cleanup_never_reissues_an_lpn_code()
    {
        var isolated = new SqlDatabase();
        await isolated.InitializeAsync();
        try
        {
            var service = LpnService(isolated);
            var firstBatch = Value(await service.IssueAsync(1, Command("lpn-cleanup")));
            string lastCode;
            await using (var firstRead = isolated.CreateDbContext())
                lastCode = await firstRead.LicensePlateNumbers.Where(x => x.BatchId == firstBatch).Select(x => x.Code).SingleAsync();

            foreach (var scriptPath in new[] { "scripts/clear-wms-operational-data.sql", "scripts/clear-database-except-identity.sql" })
            {
                var fullScriptPath = Path.Combine(AppContext.BaseDirectory, scriptPath);
                await using (var cleanup = isolated.CreateDbContext())
                    await cleanup.Database.ExecuteSqlRawAsync(await File.ReadAllTextAsync(fullScriptPath));

                var nextBatch = Value(await service.IssueAsync(1, Command("lpn-cleanup")));
                await using var nextRead = isolated.CreateDbContext();
                var nextCode = await nextRead.LicensePlateNumbers.Where(x => x.BatchId == nextBatch).Select(x => x.Code).SingleAsync();
                Assert.True(string.CompareOrdinal(nextCode, lastCode) > 0);
                lastCode = nextCode;
            }
        }
        finally
        {
            await isolated.DisposeAsync();
        }
    }

    [Fact]
    public async Task Concurrent_lpn_issuance_is_unique_and_duplicate_request_is_once_only()
    {
        var duplicateContext = Command("lpn-race");
        var duplicateService = LpnService(database.WithInterceptor(new ReceiptSaveBarrier()));
        var duplicate = await Task.WhenAll(
            duplicateService.IssueAsync(20, duplicateContext),
            duplicateService.IssueAsync(20, duplicateContext));

        Assert.Equal(Value(duplicate[0]), Value(duplicate[1]));

        var independentService = LpnService(database.WithInterceptor(new ReceiptSaveBarrier()));
        var independent = await Task.WhenAll(
            independentService.IssueAsync(40, Command("lpn-independent-a")),
            independentService.IssueAsync(40, Command("lpn-independent-b")));

        Assert.NotEqual(Value(independent[0]), Value(independent[1]));
        await using var verify = database.CreateDbContext();
        Assert.Equal(20, await verify.LicensePlateNumbers.CountAsync(x => x.BatchId == Value(duplicate[0])));
        var independentIds = independent.Select(Value).ToArray();
        var codes = await verify.LicensePlateNumbers
            .Where(x => independentIds.Contains(x.BatchId))
            .Select(x => x.Code)
            .ToListAsync();
        Assert.Equal(80, codes.Count);
        Assert.Equal(codes.Count, codes.Distinct().Count());
    }

    private static LicensePlateNumberService LpnService(IDbContextFactory<ApplicationDbContext> factory) =>
        new(new CommandExecutor(factory), factory);

    private sealed record NewCloseState(
        Guid WarehouseId, Guid OrderId, Guid LocationId, Guid SkuId, Guid LpnId);

    private sealed class PutawayPermission : IWarehousePermissionStore
    {
        public Task<bool> HasManagerRoleAsync(string userId, CancellationToken ct) =>
            Task.FromResult(false);

        public Task<bool> HasReceivingRoleAsync(string userId, CancellationToken ct) =>
            Task.FromResult(userId == "close-test");
    }

    private sealed class DeletionPermission : IWarehousePermissionStore
    {
        public Task<bool> HasManagerRoleAsync(string userId, CancellationToken ct) =>
            Task.FromResult(userId == "close-test");

        public Task<bool> HasReceivingRoleAsync(string userId, CancellationToken ct) =>
            Task.FromResult(userId == "close-test");
    }

    private async Task<Guid> PostIndependentCountAsync(
        NewCloseState state, Guid locationId, DateOnly? date)
    {
        await using var db = database.CreateDbContext();
        var balance = await db.InventoryBalances.SingleAsync(x =>
            x.StorageLocationId == locationId && x.StockKeepingUnitId == state.SkuId
            && x.ExpirationDate == date);
        var now = DateTimeOffset.UtcNow;
        var movement = Value(InventoryMovement.Create(Guid.NewGuid(), state.WarehouseId,
            locationId, null, state.SkuId, 1m, now,
            RecorderType.InventoryCount, Guid.NewGuid(), 1, "count-test", date));
        Success(movement.Post(now));
        var change = Value(balance.Adjust(-1m, now));
        var turnover = Value(InventoryTurnover.Create(Guid.NewGuid(), state.WarehouseId,
            locationId, state.SkuId, change, now, movement.Id, date));
        db.AddRange(movement, turnover);
        await db.SaveChangesAsync();
        return movement.Id;
    }

    private async Task<NewCloseState> SeedNewOpenReceivingLpnAsync(DateOnly? date = null)
    {
        var now = DateTimeOffset.UtcNow;
        var warehouse = new Warehouse { Id = Guid.NewGuid(), Name = "New close safety" };
        var sku = new StockKeepingUnit { Id = Guid.NewGuid(), Name = "Close SKU" };
        var zone = Zone(warehouse.Id, "NC", ZoneType.Receiving);
        var location = Location(warehouse.Id, zone, 1);
        var order = Value(ReceivingOrder.CreateNew(warehouse.Id, now));
        AssignTestReceivingOrderNumber(order);
        var orderItem = Value(ReceivingOrderItem.CreateForSku(order.Id, sku.Id, 5m));
        var batch = Value(LicensePlateNumberBatch.Issue(1, "close-test", now));
        var lpnId = batch.Labels.Single().Id;
        await using (var seed = database.CreateDbContext())
        {
            seed.AddRange(warehouse, sku, zone, location, order, orderItem, batch);
            await seed.SaveChangesAsync();
        }

        await using (var prepare = database.CreateDbContext())
        {
            var loadedOrder = await prepare.ReceivingOrders
                .Include(x => x.Cycles).Include(x => x.Participants)
                .Include(x => x.Items).Include(x => x.LicensePlateNumbers)
                .ThenInclude(x => x.Items)
                .SingleAsync(x => x.Id == order.Id);
            var lpn = await prepare.LicensePlateNumbers.Include(x => x.Items)
                .SingleAsync(x => x.Id == lpnId);
            Value(loadedOrder.JoinCurrentCycle(location.Id, "close-test", now));
            prepare.ReceivingOrderParticipants.Add(Assert.Single(loadedOrder.Participants));
            Assert.True(Value(loadedOrder.OpenReceivingLpn(lpn, "close-test", now)));
            Success(loadedOrder.EditReceivingLpn(
                lpn, sku.Id, date, 3m, "close-test", now, false));
            prepare.LicensePlateNumberItems.Add(Assert.Single(lpn.Items));
            await prepare.SaveChangesAsync();
        }

        return new NewCloseState(warehouse.Id, order.Id, location.Id, sku.Id, lpnId);
    }

    private static Task<OperationResult<Guid>> CloseThroughFoundationAsync(
        IDbContextFactory<ApplicationDbContext> factory, NewCloseState state, Guid requestId)
    {
        const string commandType = "test.receiving-close";
        var executor = new NewCommandExecutor(new CommandExecutionStore(factory));
        var store = new ReceivingWorkStore(factory);
        var posting = new NewPostInventoryMovement.Handler(new InventoryPostingStore());
        var hash = NewCommandExecutor.ComputeHash($"{state.OrderId:N}:{state.LpnId:N}");
        return executor.ExecuteAsync(commandType, requestId, hash, "close-test",
            async (session, ct) =>
            {
                var order = await store.LoadOrderAsync(session, state.OrderId, ct);
                var lpn = await store.LoadLpnAsync(session, state.LpnId, ct);
                if (order is null || lpn is null)
                    return OperationError.NotFound("Ордер или LPN не найдены.");
                var lpnItem = lpn.Items.Single(x => !x.IsDeleted);
                var orderItem = order.Items.Single(x => x.StockKeepingUnitId == lpnItem.StockKeepingUnitId);
                var sku = await store.LoadSkuAsync(session, lpnItem.StockKeepingUnitId, ct);
                if (sku is null)
                    return OperationError.NotFound("SKU не найден.");
                if (sku.TracksExpirationDate && lpnItem.ExpirationDate is null)
                    return OperationError.Conflict("Для SKU требуется дата срока годности.");
                if (orderItem.CurrentPlanQuantity is not decimal plan || lpnItem.Quantity > plan)
                    return OperationError.Conflict("Количество LPN превышает план.");

                var now = DateTimeOffset.UtcNow;
                var closed = lpn.CloseReceiving("close-test", now, false);
                if (!closed.IsSuccess)
                    return closed.Error!;
                store.GuardClosingInputs(session, orderItem, sku);
                var movement = InventoryMovement.Create(
                    Guid.NewGuid(), state.WarehouseId, null, state.LocationId,
                    state.SkuId, lpnItem.Quantity, now, RecorderType.ReceivingOrder,
                    state.OrderId, 1, "close-test", lpnItem.ExpirationDate, lpn.Id);
                if (!movement.IsSuccess)
                    return movement.Error!;
                var posted = await posting.HandleAsync(new(
                    movement.Value!, InventoryPostingKind.Receipt, now), session, ct);
                if (!posted.IsSuccess)
                    return posted.Error!;
                var audit = WarehouseAuditEvent.Create(commandType, now, "close-test", null,
                    state.OrderId, lpn.Id, state.SkuId, null,
                    "{\"status\":\"Open\"}", "{\"status\":\"Closed\"}",
                    [movement.Value!.Id], commandUserId: "close-test",
                    commandType: commandType, requestId: requestId);
                if (!audit.IsSuccess)
                    return audit.Error!;
                return new NewCommandExecutionResult(lpn.Id, [audit.Value!]);
            }, default);
    }

    private sealed class CompletionReceiptBarrier : SaveChangesInterceptor
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<CommandReceipt>().Any(x => x.State == EntityState.Added))
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            }
            return result;
        }
    }

}
