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
public sealed class StockAvailabilitySafetyTests(SqlDatabase database)
{
    private const string UserId = "stock-worker";
    private const string ReserveCommand = "test.stock.reserve";

    [Fact]
    public async Task Dates_shortages_and_own_reservations_are_accounted_separately()
    {
        var (warehouseId, locationId, skuId) = await SeedAsync();
        var assemblyId = Guid.NewGuid();
        var date = new DateOnly(2027, 1, 1);
        await using (var db = database.CreateDbContext())
        {
            db.Add(Value(InventoryBalance.Create(Guid.NewGuid(), warehouseId, locationId,
                skuId, 5, DateTimeOffset.UtcNow, date)));
            db.Add(Reservation(warehouseId, locationId, skuId, assemblyId, Guid.NewGuid(), 7));
            db.Add(Value(StockShortage.Report(Guid.NewGuid(), warehouseId, locationId, skuId,
                date, StockShortageOrigin.Picking, Guid.NewGuid(), 5, 3, null,
                UserId, DateTimeOffset.UtcNow)));
            await db.SaveChangesAsync();
        }

        var store = new StockAvailabilityStore(database);
        var groups = await store.ReadSkuAsync(warehouseId, skuId, default);
        var undated = Assert.Single(groups.Where(x => x.Group.ExpirationDate is null));
        Assert.Equal(10m, undated.AccountingQuantity);
        Assert.Equal(3m, undated.AvailableQuantity);
        Assert.Equal(7m, undated.AvailableForReservedIssue(assemblyId));
        Assert.Equal(3m, Assert.Single(groups.Where(x => x.Group.ExpirationDate == date)).AvailableQuantity);

        await using (var db = database.CreateDbContext())
        {
            db.Add(Value(StorageLocationLock.CreateForPicking(
                locationId, assemblyId, DateTimeOffset.UtcNow, UserId)));
            await db.SaveChangesAsync();
        }

        groups = await store.ReadSkuAsync(warehouseId, skuId, default);
        Assert.All(groups, x => Assert.Equal(0m, x.AvailableQuantity));
        undated = Assert.Single(groups.Where(x => x.Group.ExpirationDate is null));
        Assert.Equal(3m, undated.AvailableForNewPick(assemblyId));
        Assert.Equal(7m, undated.AvailableForReservedIssue(assemblyId));
        Assert.Equal(0m, undated.AvailableForReservedIssue(Guid.NewGuid()));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Two_contending_requests_cannot_reserve_the_same_stock(
        bool duplicateRequest, bool postingRequest)
    {
        var (warehouseId, locationId, skuId) = await SeedAsync();
        var store = new StockAvailabilityStore(database);
        var executor = new CommandExecutor(new CommandExecutionStore(database));
        var requestId = Guid.NewGuid();
        var assemblyId = Guid.NewGuid();
        var entryId = Guid.NewGuid();
        var firstHasLock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = ExecuteReservation(requestId, entryId, assemblyId, firstAttempt: true);
        await firstHasLock.Task.WaitAsync(TimeSpan.FromSeconds(20));
        var second = ExecuteReservation(duplicateRequest ? requestId : Guid.NewGuid(),
            duplicateRequest ? entryId : Guid.NewGuid(),
            duplicateRequest ? assemblyId : Guid.NewGuid(), firstAttempt: false);
        try
        {
            await secondEntered.Task.WaitAsync(TimeSpan.FromSeconds(20));
        }
        finally
        {
            releaseFirst.TrySetResult();
        }

        Assert.Equal(entryId, Value(await first));
        var secondResult = await second;
        if (duplicateRequest)
            Assert.Equal(entryId, Value(secondResult));
        else
            Error(secondResult, OperationErrorType.Conflict);

        await using var verify = database.CreateDbContext();
        Assert.Single(await verify.PickingReservations.Where(x => x.WarehouseId == warehouseId).ToArrayAsync());
        Assert.Equal(1, await verify.CommandReceipts.CountAsync(x => x.ResultResourceId == entryId));
        Assert.Equal(10m, (await verify.InventoryBalances.SingleAsync(x => x.WarehouseId == warehouseId)).Quantity);
        Assert.False(await verify.InventoryMovements.AnyAsync(x => x.WarehouseId == warehouseId));

        Task<OperationResult<Guid>> ExecuteReservation(
            Guid commandId, Guid pickId, Guid ownerId, bool firstAttempt) =>
            executor.ExecuteAsync(ReserveCommand, commandId,
                CommandExecutor.ComputeHash($"{pickId:N}:{ownerId:N}:10"), UserId,
                async (context, ct) =>
                {
                    if (!firstAttempt)
                        secondEntered.TrySetResult();
                    if (!firstAttempt && postingRequest)
                    {
                        var now = DateTimeOffset.UtcNow;
                        var movement = Value(InventoryMovement.Create(Guid.NewGuid(), warehouseId,
                            locationId, null, skuId, 10, now, RecorderType.StockCorrection,
                            ownerId, 1, UserId));
                        var posted = await new PostInventoryMovement.Handler(new InventoryPostingStore())
                            .HandleAsync(new(movement, InventoryPostingKind.StockCorrection, now.AddSeconds(1)), context, ct);
                        if (!posted.IsSuccess)
                            return posted.Error!;
                        return Effect(movement.Id, skuId, ReserveCommand, commandId);
                    }
                    var reserved = await store.ReserveAsync(context,
                        Reservation(warehouseId, locationId, skuId, ownerId, pickId, 10), ct);
                    if (!reserved.IsSuccess)
                        return reserved.Error!;
                    if (firstAttempt)
                    {
                        firstHasLock.TrySetResult();
                        await releaseFirst.Task.WaitAsync(TimeSpan.FromSeconds(20), ct);
                    }
                    return Effect(pickId, skuId, ReserveCommand, commandId);
                }, default);
    }

    [Fact]
    public async Task Whole_location_hold_rejects_reservations_of_another_sku()
    {
        var (warehouseId, locationId, skuId) = await SeedAsync();
        var otherSku = new StockKeepingUnit { Id = Guid.NewGuid(), Name = "Other reserved SKU" };
        var assemblyId = Guid.NewGuid();
        await using (var db = database.CreateDbContext())
        {
            db.Add(otherSku);
            db.Add(Value(InventoryBalance.Create(Guid.NewGuid(), warehouseId, locationId,
                otherSku.Id, 3, DateTimeOffset.UtcNow)));
            db.Add(Reservation(warehouseId, locationId, otherSku.Id, assemblyId, Guid.NewGuid(), 3));
            await db.SaveChangesAsync();
        }

        var store = new StockAvailabilityStore(database);
        var executor = new CommandExecutor(new CommandExecutionStore(database));
        var requestId = Guid.NewGuid();
        var held = await executor.ExecuteAsync("test.stock.hold", requestId,
            CommandExecutor.ComputeHash(locationId.ToString()), UserId, async (context, ct) =>
            {
                var hold = Value(StorageLocationLock.CreateForReplenishment(
                    locationId, Guid.NewGuid(), DateTimeOffset.UtcNow, UserId));
                var result = await store.HoldAsync(context, warehouseId, hold, ct);
                if (!result.IsSuccess)
                    return result.Error!;
                return Effect(locationId, skuId, "test.stock.hold", requestId);
            }, default);

        Error(held, OperationErrorType.Conflict);
        await using var verify = database.CreateDbContext();
        Assert.False(await verify.StorageLocationLocks.AnyAsync(x => x.StorageLocationId == locationId));
        Assert.Equal(0, (await verify.StorageLocations.SingleAsync(x => x.Id == locationId)).OperationalRevision);
    }

    [Fact]
    public async Task Rejected_command_rolls_back_reservation_and_releases_location_lock()
    {
        var (warehouseId, locationId, skuId) = await SeedAsync();
        var store = new StockAvailabilityStore(database);
        var executor = new CommandExecutor(new CommandExecutionStore(database));
        var requestId = Guid.NewGuid();
        var result = await executor.ExecuteAsync(ReserveCommand, requestId,
            CommandExecutor.ComputeHash(requestId.ToString()), UserId, async (context, ct) =>
            {
                Success(await store.ReserveAsync(context,
                    Reservation(warehouseId, locationId, skuId, Guid.NewGuid(), Guid.NewGuid(), 7), ct));
                var second = await store.ReserveAsync(context,
                    Reservation(warehouseId, locationId, skuId, Guid.NewGuid(), Guid.NewGuid(), 4), ct);
                Assert.False(second.IsSuccess);
                return second.Error!;
            }, default);
        Error(result, OperationErrorType.Conflict);

        await using (var verify = database.CreateDbContext())
        {
            Assert.False(await verify.PickingReservations.AnyAsync(x => x.WarehouseId == warehouseId));
            Assert.Equal(0, (await verify.StorageLocations.SingleAsync(x => x.Id == locationId)).OperationalRevision);
        }
        var availability = Assert.Single(await store.ReadSkuAsync(warehouseId, skuId, default));
        Assert.Equal(10m, availability.AvailableQuantity);
    }

    private async Task<(Guid WarehouseId, Guid LocationId, Guid SkuId)> SeedAsync()
    {
        var warehouse = new Warehouse { Id = Guid.NewGuid(), Name = "Stock availability" };
        var sku = new StockKeepingUnit { Id = Guid.NewGuid(), Name = "Reserved stock" };
        var zone = Zone(warehouse.Id, "AV", ZoneType.Storage);
        var location = Location(warehouse.Id, zone, 1, allowsMultipleSkus: true);
        await using var db = database.CreateDbContext();
        db.AddRange(warehouse, sku, zone, location, Balance(warehouse.Id, location.Id, sku.Id, 10));
        await db.SaveChangesAsync();
        return (warehouse.Id, location.Id, sku.Id);
    }

    private static PickingReservation Reservation(
        Guid warehouseId, Guid locationId, Guid skuId, Guid assemblyId, Guid entryId, decimal quantity) =>
        Value(PickingReservation.Create(entryId, assemblyId, warehouseId, locationId, skuId,
            null, quantity, UserId, DateTimeOffset.UtcNow));

    private static CommandExecutionResult Effect(Guid resourceId, Guid skuId, string commandType, Guid requestId) =>
        new(resourceId, [Value(WarehouseAuditEvent.Create(commandType, DateTimeOffset.UtcNow,
            UserId, null, null, null, skuId, null, null, null,
            commandUserId: UserId, commandType: commandType, requestId: requestId))]);
}
