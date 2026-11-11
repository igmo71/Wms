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
public sealed class PickingReturnSafetyTests(SqlDatabase database)
{
    private const string Worker = "picking-worker";
    private const string NewWorker = "new-worker";

    [Fact]
    public async Task Corrected_shortage_preserves_picked_reserve_until_scanned_return()
    {
        var warehouse = new Warehouse { Id = Guid.NewGuid(), Name = "Return after shortage" };
        var sku = new StockKeepingUnit { Id = Guid.NewGuid(), Name = "Picked SKU" };
        var zone = Zone(warehouse.Id, "STOCK", ZoneType.Storage);
        var location = Location(warehouse.Id, zone, 1);
        var sourceId = Guid.NewGuid();
        var receiverId = Guid.NewGuid();
        var source = Value(ShippingOrder.Create(SourceImport(sourceId,
            warehouse.Id, sku.Id, receiverId, ShippingOrderStatus.Prepared),
            DateTimeOffset.UtcNow));
        Success(source.ApplyCurrentSource(SourceImport(sourceId,
            warehouse.Id, sku.Id, receiverId, ShippingOrderStatus.ReadyForPicking),
            DateTimeOffset.UtcNow));
        var assembly = PickingAssembly(warehouse.Id, Worker);
        Value(assembly.AttachSource(sourceId, "{\"version\":1}",
            new Dictionary<Guid, decimal> { [sku.Id] = 10 }, Worker,
            DateTimeOffset.UtcNow));
        var startedAt = DateTimeOffset.UtcNow;
        var leaseId = Guid.NewGuid();
        Success(assembly.FreezeForStart(Worker, startedAt));
        Success(assembly.ClaimStart(leaseId, startedAt, startedAt.AddMinutes(10)));
        Success(assembly.RecordSourceStartResult(leaseId, sourceId, startedAt, null));
        Success(assembly.CompleteStart(leaseId, startedAt));
        Success(assembly.ReleaseStart(leaseId));
        await using (var seed = database.CreateDbContext())
        {
            seed.AddRange(warehouse, sku, zone, location, source, assembly,
                Balance(warehouse.Id, location.Id, sku.Id, 10));
            await seed.SaveChangesAsync();
        }

        var permission = new WarehousePermission(new BothPermission());
        var executor = new CommandExecutor(new CommandExecutionStore(database));
        var assemblyStore = new PickingAssemblyStore(database);
        var stock = new StockAvailabilityStore(database);
        var pick = new ConfirmPick(executor, assemblyStore, stock, stock,
            permission, TimeProvider.System);
        var report = new ReportPickingShortage(executor, assemblyStore, stock,
            stock, stock, permission, TimeProvider.System);
        var resolve = new ResolveStockShortage(executor, stock,
            new PostInventoryMovement.Handler(new InventoryPostingStore()),
            permission, TimeProvider.System);
        var reduce = new ReducePick(executor, assemblyStore, stock,
            permission, TimeProvider.System);

        var takeOver = new TakeOverPickingAssembly(executor, assemblyStore,
            permission, TimeProvider.System);
        Value(await takeOver.ExecuteAsync(new TakeOverPickingAssemblyRequest(
            Guid.NewGuid(), assembly.Id, assembly.OperationalRevision, NewWorker)));
        Error(await pick.ExecuteAsync(new ConfirmPickRequest(Guid.NewGuid(),
            assembly.Id, assembly.OperationalRevision, location.Code,
            sku.Id, null, 6, Worker)), OperationErrorType.Conflict);
        await using (var afterConflict = database.CreateDbContext())
        {
            Assert.False(await afterConflict.PickingEntries.AnyAsync(x =>
                x.PickingAssemblyId == assembly.Id));
            Assert.False(await afterConflict.PickingReservations.AnyAsync(x =>
                x.PickingAssemblyId == assembly.Id));
        }
        var afterTakeOver = (await assemblyStore.ReadAssemblyAsync(assembly.Id,
            CancellationToken.None))!;

        var entryId = Value(await pick.ExecuteAsync(new ConfirmPickRequest(
            Guid.NewGuid(), assembly.Id, afterTakeOver.OperationalRevision,
            location.Code, sku.Id, null, 6, NewWorker)));
        Assert.Equal(4m, (await stock.ReadSkuAsync(warehouse.Id, sku.Id,
            CancellationToken.None)).Single().AvailableQuantity);
        var afterPick = (await assemblyStore.ReadAssemblyAsync(assembly.Id,
            CancellationToken.None))!;
        var shortageId = Value(await report.ExecuteAsync(
            new ReportPickingShortageRequest(Guid.NewGuid(), assembly.Id,
                afterPick.OperationalRevision, location.Code, sku.Id,
                null, 4, 0, NewWorker)));
        Value(await resolve.ExecuteAsync(new ResolveStockShortageRequest(
            Guid.NewGuid(), shortageId, true, "Недостача проверена", "manager")));

        var beforeReturn = (await stock.ReadSkuAsync(warehouse.Id, sku.Id,
            CancellationToken.None)).Single();
        Assert.Equal(6m, beforeReturn.AccountingQuantity);
        Assert.Equal(6m, beforeReturn.ReservedQuantity);
        Assert.Equal(0m, beforeReturn.AvailableQuantity);
        var afterShortage = (await assemblyStore.ReadAssemblyAsync(assembly.Id,
            CancellationToken.None))!;
        var returnRequest = new ReducePickRequest(Guid.NewGuid(), assembly.Id,
            entryId, afterShortage.OperationalRevision, location.Code,
            6, true, NewWorker);
        Assert.Equal(entryId, Value(await reduce.ExecuteAsync(returnRequest)));
        Assert.Equal(entryId, Value(await reduce.ExecuteAsync(returnRequest)));

        await using var final = database.CreateDbContext();
        Assert.Equal(6m, (await final.InventoryBalances.SingleAsync(x =>
            x.StorageLocationId == location.Id)).Quantity);
        Assert.Equal(0m, (await final.PickingEntries.SingleAsync(x =>
            x.Id == entryId)).Quantity);
        Assert.Equal(0m, (await final.PickingReservations.SingleAsync(x =>
            x.PickingEntryId == entryId)).Quantity);
        Assert.Equal(1, await final.InventoryMovements.CountAsync(x =>
            x.RecorderId == shortageId));
        Assert.Equal(1, await final.CommandReceipts.CountAsync(x =>
            x.RequestId == returnRequest.RequestId));
    }

    private static ShippingOrderImportSnapshot SourceImport(Guid sourceId,
        Guid warehouseId, Guid skuId, Guid receiverId,
        ShippingOrderStatus status) => new(
            sourceId, false, true, "РО 1С", DateTime.UtcNow, warehouseId,
            null, status, ShippingOrderQueue.Unknown, null, null,
            WarehouseOperation.CustomerShipment, receiverId, PartyType.Partner,
            [new ShippingOrderItemImportSnapshot(1, skuId, 10, 10,
                ShippingOrderAction.PickUp)], []);

    private sealed class BothPermission : IWarehousePermissionStore
    {
        public Task<bool> HasManagerRoleAsync(string userId, CancellationToken ct) =>
            Task.FromResult(true);

        public Task<bool> HasOperatorRoleAsync(string userId, CancellationToken ct) =>
            Task.FromResult(true);
    }
}
