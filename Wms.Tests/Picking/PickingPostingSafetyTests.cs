using Microsoft.EntityFrameworkCore;
using Wms.Application.Authorization;
using Wms.Application.CommandExecution;
using Wms.Application.Inventory;
using Wms.Application.Picking;
using Wms.Common;
using Wms.Data;
using Wms.Domain;
using Wms.Domain.Enums;
using Wms.Tests.Infrastructure;
using static Wms.Tests.Infrastructure.TestSupport;

namespace Wms.Tests.Picking;

[Collection(SqlDatabaseCollection.Name)]
public sealed class PickingPostingSafetyTests(SqlDatabase database)
{
    private const string UserId = "picking-worker";

    [Fact]
    public async Task Failed_second_issue_rolls_back_first_and_same_request_can_retry_once()
    {
        var warehouse = new Warehouse { Id = Guid.NewGuid(), Name = "Picking closure" };
        var sku = new StockKeepingUnit { Id = Guid.NewGuid(), Name = "Picked SKU" };
        var zone = Zone(warehouse.Id, "STOCK", ZoneType.Storage);
        var first = Location(warehouse.Id, zone, 1);
        var second = Location(warehouse.Id, zone, 2);
        var sourceId = Guid.NewGuid();
        var receiverId = Guid.NewGuid();
        var sourceSnapshot = Source(sourceId, warehouse.Id, sku.Id, receiverId);
        var source = Value(ShippingOrder.Create(SourceImport(sourceSnapshot,
            ShippingOrderStatus.Prepared), DateTimeOffset.UtcNow));
        Success(source.ApplyCurrentSource(SourceImport(sourceSnapshot,
            ShippingOrderStatus.ReadyForPicking), DateTimeOffset.UtcNow));
        Success(source.RecordSourceCheck(sourceSnapshot.RawDocumentJson, null,
            DateTimeOffset.UtcNow));

        var assembly = PickingAssembly(warehouse.Id, UserId);
        Value(assembly.AttachSource(sourceId, sourceSnapshot.RawDocumentJson,
            new Dictionary<Guid, decimal> { [sku.Id] = 10 }, UserId,
            DateTimeOffset.UtcNow));
        var leaseId = Guid.NewGuid();
        var startedAt = DateTimeOffset.UtcNow;
        Success(assembly.FreezeForStart(UserId, startedAt));
        Success(assembly.ClaimStart(leaseId, startedAt, startedAt.AddMinutes(10)));
        Success(assembly.RecordSourceStartResult(leaseId, sourceId, startedAt, null));
        Success(assembly.CompleteStart(leaseId, startedAt));

        var firstEntry = Value(PickingEntry.Create(assembly.Id, warehouse.Id,
            first.Id, sku.Id, null, 4, UserId, DateTimeOffset.UtcNow));
        var secondEntry = Value(PickingEntry.Create(assembly.Id, warehouse.Id,
            second.Id, sku.Id, null, 6, UserId, DateTimeOffset.UtcNow));
        var firstBalance = Value(InventoryBalance.Create(Guid.NewGuid(), warehouse.Id,
            first.Id, sku.Id, 4, DateTimeOffset.UtcNow));
        var secondBalance = Value(InventoryBalance.Create(Guid.NewGuid(), warehouse.Id,
            second.Id, sku.Id, 5, DateTimeOffset.UtcNow));
        await using (var seed = database.CreateDbContext())
        {
            seed.AddRange(warehouse, sku, zone, first, second, source, assembly,
                firstEntry, secondEntry, firstBalance, secondBalance);
            seed.AddRange(
                Value(PickingReservation.Create(firstEntry.Id, assembly.Id,
                    warehouse.Id, first.Id, sku.Id, null, 4, UserId,
                    DateTimeOffset.UtcNow)),
                Value(PickingReservation.Create(secondEntry.Id, assembly.Id,
                    warehouse.Id, second.Id, sku.Id, null, 6, UserId,
                    DateTimeOffset.UtcNow)));
            await seed.SaveChangesAsync();
        }

        var store = new PickingAssemblyStore(database);
        var permission = new WarehousePermission(new OperatorPermission());
        var checker = new CheckPickingClosure(store,
            new SynchronizeShippingSource(new FixedReader(sourceSnapshot),
                new ShippingSourceStore(database), TimeProvider.System),
            new FixedParser(sourceSnapshot), new AllocatePickingFact(),
            permission, TimeProvider.System);
        var close = new ClosePickingAssembly(
            new CommandExecutor(new CommandExecutionStore(database)), store,
            checker, new AllocatePickingFact(),
            new PostInventoryMovement.Handler(new InventoryPostingStore()),
            new ReplenishmentStore(database),
            permission, TimeProvider.System);
        var request = new ClosePickingAssemblyRequest(Guid.NewGuid(), assembly.Id,
            assembly.OperationalRevision, UserId);

        Error(await close.ExecuteAsync(request), OperationErrorType.Conflict);
        await using (var verify = database.CreateDbContext())
        {
            Assert.Empty(await verify.InventoryMovements.Where(x =>
                x.RecorderId == assembly.Id).ToArrayAsync());
            Assert.Empty(await verify.PickingClosures.Where(x =>
                x.PickingAssemblyId == assembly.Id).ToArrayAsync());
            Assert.Empty(await verify.ShippingExchangeTargets.Where(x =>
                x.PickingAssemblyId == assembly.Id).ToArrayAsync());
            Assert.False(await verify.CommandReceipts.AnyAsync(x =>
                x.RequestId == request.RequestId));
            Assert.Equal(4m, (await verify.InventoryBalances.SingleAsync(x =>
                x.StorageLocationId == first.Id)).Quantity);
            Assert.Equal(5m, (await verify.InventoryBalances.SingleAsync(x =>
                x.StorageLocationId == second.Id)).Quantity);
            Assert.Equal(2, await verify.PickingReservations.CountAsync(x =>
                x.PickingAssemblyId == assembly.Id && x.CompletedAtUtc == null));
            Assert.Equal(PickingAssemblyStatus.InProgress,
                (await verify.PickingAssemblies.SingleAsync(x => x.Id == assembly.Id)).Status);
        }

        await using (var repair = database.CreateDbContext())
        {
            var balance = await repair.InventoryBalances.SingleAsync(x =>
                x.StorageLocationId == second.Id);
            Value(balance.Adjust(1, DateTimeOffset.UtcNow));
            await repair.SaveChangesAsync();
        }
        var reservedStock = await new StockAvailabilityStore(database).ReadSkuAsync(
            warehouse.Id, sku.Id, CancellationToken.None);
        Assert.Equal(10m, reservedStock.Sum(x => x.ReservedQuantity));
        Assert.All(reservedStock, x => Assert.Equal(0m, x.AvailableQuantity));
        Assert.Equal(assembly.Id, Value(await close.ExecuteAsync(request)));
        Assert.Equal(assembly.Id, Value(await close.ExecuteAsync(request)));

        await using var final = database.CreateDbContext();
        Assert.Equal(2, await final.InventoryMovements.CountAsync(x =>
            x.RecorderId == assembly.Id));
        Assert.Equal(2, await final.InventoryTurnovers.CountAsync(x =>
            x.InventoryMovement!.RecorderId == assembly.Id));
        Assert.All(await final.InventoryBalances.Where(x =>
            x.WarehouseId == warehouse.Id).ToArrayAsync(), x => Assert.Equal(0m, x.Quantity));
        Assert.Equal(2, await final.PickingReservations.CountAsync(x =>
            x.PickingAssemblyId == assembly.Id && x.CompletedAtUtc != null));
        Assert.Single(await final.PickingClosures.Where(x =>
            x.PickingAssemblyId == assembly.Id).ToArrayAsync());
        Assert.Single(await final.ShippingExchangeTargets.Where(x =>
            x.PickingAssemblyId == assembly.Id).ToArrayAsync());
        Assert.Equal(1, await final.CommandReceipts.CountAsync(x =>
            x.RequestId == request.RequestId));
        Assert.Equal(PickingAssemblyStatus.Closed,
            (await final.PickingAssemblies.SingleAsync(x => x.Id == assembly.Id)).Status);
    }

    [Fact]
    public async Task Stale_close_after_takeover_posts_nothing_and_zero_fact_closes_without_movement()
    {
        var warehouse = new Warehouse { Id = Guid.NewGuid(), Name = "Zero picking close" };
        var sku = new StockKeepingUnit { Id = Guid.NewGuid(), Name = "Unpicked SKU" };
        var sourceId = Guid.NewGuid();
        var snapshot = Source(sourceId, warehouse.Id, sku.Id, Guid.NewGuid());
        var source = Value(ShippingOrder.Create(SourceImport(snapshot,
            ShippingOrderStatus.Prepared), DateTimeOffset.UtcNow));
        Success(source.ApplyCurrentSource(SourceImport(snapshot,
            ShippingOrderStatus.ReadyForPicking), DateTimeOffset.UtcNow));
        Success(source.RecordSourceCheck(snapshot.RawDocumentJson, null,
            DateTimeOffset.UtcNow));
        var assembly = PickingAssembly(warehouse.Id, UserId);
        Value(assembly.AttachSource(sourceId, snapshot.RawDocumentJson,
            new Dictionary<Guid, decimal> { [sku.Id] = 10 }, UserId,
            DateTimeOffset.UtcNow));
        var startedAt = DateTimeOffset.UtcNow;
        var leaseId = Guid.NewGuid();
        Success(assembly.FreezeForStart(UserId, startedAt));
        Success(assembly.ClaimStart(leaseId, startedAt, startedAt.AddMinutes(10)));
        Success(assembly.RecordSourceStartResult(leaseId, sourceId, startedAt, null));
        Success(assembly.CompleteStart(leaseId, startedAt));
        Success(assembly.ReleaseStart(leaseId));
        await using (var seed = database.CreateDbContext())
        {
            seed.AddRange(warehouse, sku, source, assembly);
            await seed.SaveChangesAsync();
        }

        var permission = new WarehousePermission(new BothPermission());
        var store = new PickingAssemblyStore(database);
        var executor = new CommandExecutor(new CommandExecutionStore(database));
        var checker = new CheckPickingClosure(store,
            new SynchronizeShippingSource(new FixedReader(snapshot),
                new ShippingSourceStore(database), TimeProvider.System),
            new FixedParser(snapshot), new AllocatePickingFact(), permission,
            TimeProvider.System);
        var close = new ClosePickingAssembly(executor, store, checker,
            new AllocatePickingFact(),
            new PostInventoryMovement.Handler(new InventoryPostingStore()),
            new ReplenishmentStore(database),
            permission, TimeProvider.System);
        var takeOver = new TakeOverPickingAssembly(executor, store, permission,
            TimeProvider.System);
        var originalRevision = assembly.OperationalRevision;
        var stale = new ClosePickingAssemblyRequest(Guid.NewGuid(), assembly.Id,
            originalRevision, "manager", ManagerOverride: true,
            DecisionReason: "Физический факт нулевой",
            ReviewedSourcesById: new Dictionary<Guid, string>
            {
                [sourceId] = snapshot.RawDocumentJson
            });

        Value(await takeOver.ExecuteAsync(new TakeOverPickingAssemblyRequest(
            Guid.NewGuid(), assembly.Id, originalRevision, "second-worker")));
        Error(await close.ExecuteAsync(stale), OperationErrorType.Conflict);
        await using (var verify = database.CreateDbContext())
        {
            Assert.Empty(await verify.InventoryMovements.Where(x =>
                x.RecorderId == assembly.Id).ToArrayAsync());
            Assert.False(await verify.PickingClosures.AnyAsync(x =>
                x.PickingAssemblyId == assembly.Id));
        }

        var freshRevision = (await store.ReadAssemblyAsync(assembly.Id,
            CancellationToken.None))!.OperationalRevision;
        Value(await close.ExecuteAsync(stale with
        {
            RequestId = Guid.NewGuid(), ExpectedRevision = freshRevision
        }));
        await using var final = database.CreateDbContext();
        Assert.Empty(await final.InventoryMovements.Where(x =>
            x.RecorderId == assembly.Id).ToArrayAsync());
        Assert.Single(await final.PickingClosures.Where(x =>
            x.PickingAssemblyId == assembly.Id).ToArrayAsync());
        var allocation = await final.PickingAllocations.SingleAsync(x =>
            x.PickingAssemblyId == assembly.Id);
        Assert.Equal(0m, allocation.Quantity);
        Assert.Single(await final.ShippingExchangeTargets.Where(x =>
            x.PickingAssemblyId == assembly.Id).ToArrayAsync());
    }

    private static ShippingSourceSnapshot Source(Guid sourceId, Guid warehouseId,
        Guid skuId, Guid receiverId) => new(
            RefKey: sourceId, RawDocumentJson: "{\"version\":1}", Posted: true,
            DeletionMark: false, Number: "РО 1С", Date: DateTime.UtcNow,
            WarehouseId: warehouseId, Comment: null, RawStatus: null,
            Status: ShippingOrderStatus.ReadyForPicking, RawWarehouseOperation: null,
            WarehouseOperation: WarehouseOperation.CustomerShipment,
            ReceiverId: receiverId, RawReceiverType: null,
            ReceiverType: PartyType.Partner, RawQueue: null,
            Queue: ShippingOrderQueue.Unknown, PlannedShippingDate: null,
            DeliveryDirectionId: null,
            Items: [new ShippingSourceItem(1, skuId, "10", 10, "10", 10,
                null, ShippingOrderAction.PickUp, false, false, "{}")],
            BasisItems: []);

    private static ShippingOrderImportSnapshot SourceImport(
        ShippingSourceSnapshot source, ShippingOrderStatus status) => new(
            source.RefKey, false, true, source.Number, source.Date!.Value,
            source.WarehouseId!.Value, null, status, source.Queue, null, null,
            source.WarehouseOperation, source.ReceiverId!.Value,
            source.ReceiverType!.Value,
            [new ShippingOrderItemImportSnapshot(1,
                source.Items[0].StockKeepingUnitId!.Value, 10, 10,
                ShippingOrderAction.PickUp)], []);

    private sealed class FixedReader(ShippingSourceSnapshot source)
        : IShippingSourceReader
    {
        public Task<OperationResult<ShippingSourceSnapshot>> ReadAsync(
            Guid refKey, CancellationToken ct) =>
            Task.FromResult<OperationResult<ShippingSourceSnapshot>>(source);
    }

    private sealed class FixedParser(ShippingSourceSnapshot source)
        : IShippingStoredSnapshotParser
    {
        public OperationResult<ShippingSourceSnapshot> Parse(Guid refKey,
            string storedDocumentJson) => source;
    }

    private sealed class OperatorPermission : IWarehousePermissionStore
    {
        public Task<bool> HasManagerRoleAsync(string userId, CancellationToken ct) =>
            Task.FromResult(false);

        public Task<bool> HasOperatorRoleAsync(string userId, CancellationToken ct) =>
            Task.FromResult(true);
    }

    private sealed class BothPermission : IWarehousePermissionStore
    {
        public Task<bool> HasManagerRoleAsync(string userId, CancellationToken ct) =>
            Task.FromResult(true);

        public Task<bool> HasOperatorRoleAsync(string userId, CancellationToken ct) =>
            Task.FromResult(true);
    }
}
