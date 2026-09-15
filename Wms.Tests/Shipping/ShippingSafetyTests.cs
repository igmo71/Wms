using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Wms.Application.Commands;
using Wms.Application.Inventory.Movements;
using Wms.Application.ShippingOrders;
using Wms.Common;
using Wms.Data;
using Wms.Domain;
using Wms.Domain.Enums;
using Wms.Tests.Infrastructure;
using static Wms.Tests.Infrastructure.TestSupport;

namespace Wms.Tests.Shipping;

[Collection(SqlDatabaseCollection.Name)]
public sealed class ShippingSafetyTests(SqlDatabase database)
{
    [Fact]
    public async Task Picking_completion_and_shipping_post_each_stock_leg_once()
    {
        var setup = await CreateShippingSetup();
        var source = new MutableShippingSource { Snapshot = setup.Snapshot };
        var sink = new MutableShippingSink(source);
        var shipping = ShippingService(database, source, sink);
        var picking = new PickingCommandService(new CommandExecutor(database), NullLogger<PickingCommandService>.Instance);

        Value(await shipping.StartPickingAsync(new(setup.OrderId, setup.ShippingLocationId), Command("shipping")));
        Value(await picking.AddPickingMovementAsync(new(setup.OrderId, 1, setup.StorageLocationId, 5m), Command("shipping")));
        source.Snapshot = setup.Snapshot with { Status = ShippingOrderStatus.ReadyForPicking };

        var readyContext = Command("shipping");
        Value(await shipping.SetReadyForShipmentAsync(setup.OrderId, readyContext));
        var callsAfterReady = source.Calls + sink.Calls;
        Value(await shipping.SetReadyForShipmentAsync(setup.OrderId, readyContext));
        Assert.Equal(callsAfterReady, source.Calls + sink.Calls);

        var shipContext = Command("shipping");
        Value(await shipping.SetShippedAsync(setup.OrderId, shipContext));
        var callsAfterShipping = source.Calls + sink.Calls;
        Value(await shipping.SetShippedAsync(setup.OrderId, shipContext));
        Assert.Equal(callsAfterShipping, source.Calls + sink.Calls);

        await using var verify = database.CreateDbContext();
        Assert.Equal(ShippingOrderStatus.Shipped, (await verify.ShippingOrders.SingleAsync(x => x.Id == setup.OrderId)).Status);
        Assert.Equal(95m, (await verify.InventoryBalances.SingleAsync(x => x.Id == setup.StorageBalanceId)).Quantity);
        Assert.Equal(0m, await verify.InventoryBalances.Where(x => x.StorageLocationId == setup.ShippingLocationId).SumAsync(x => x.Quantity));
        Assert.Equal(2, await verify.InventoryMovements.CountAsync(x => x.RecorderId == setup.OrderId && x.PostedAtUtc != null));
        Assert.Equal(3, await verify.InventoryTurnovers.CountAsync(x => x.InventoryMovement!.RecorderId == setup.OrderId));
    }

    [Fact]
    public async Task Posted_shipping_rollback_is_atomic_and_compensates_exactly_once()
    {
        var setup = await CreateShippingSetup();
        await using (var prepare = database.CreateDbContext())
        {
            var order = await prepare.ShippingOrders.SingleAsync(x => x.Id == setup.OrderId);
            Success(order.SetShippingLocation(setup.ShippingLocationId));
            Success(order.SetReadyForPicking(DateTimeOffset.UtcNow, "rollback"));
            await prepare.SaveChangesAsync();
        }

        var picking = new PickingCommandService(new CommandExecutor(database), NullLogger<PickingCommandService>.Instance);
        Value(await picking.AddPickingMovementAsync(new(setup.OrderId, 1, setup.StorageLocationId, 5m), Command("rollback")));
        var preparationSource = new FixedShippingSource(setup.Snapshot with { Status = ShippingOrderStatus.ReadyForPicking });
        Value(await ShippingService(database, preparationSource, new PermissiveShippingSink())
            .SetReadyForShipmentAsync(setup.OrderId, Command("rollback")));

        await AdjustBalanceAtLocation(setup.ShippingLocationId, setup.SkuId, -5m);
        var rollbackContext = Command("rollback");
        var rollback = ShippingService(database, new RejectingShippingSource(), new RejectingShippingSink());

        Assert.False((await rollback.RollbackAsync(new(setup.OrderId, "stock recovery"), rollbackContext)).IsSuccess);
        await using (var failed = database.CreateDbContext())
        {
            Assert.Equal(ShippingOrderStatus.ReadyForShipment, (await failed.ShippingOrders.SingleAsync(x => x.Id == setup.OrderId)).Status);
            Assert.Equal(1, await failed.InventoryMovements.CountAsync(x => x.RecorderId == setup.OrderId));
            Assert.Equal(2, await failed.InventoryTurnovers.CountAsync(x => x.InventoryMovement!.RecorderId == setup.OrderId));
            Assert.False(await failed.CommandReceipts.AnyAsync(x => x.RequestId == rollbackContext.RequestId));
        }

        await AdjustBalanceAtLocation(setup.ShippingLocationId, setup.SkuId, 5m);
        Value(await rollback.RollbackAsync(new(setup.OrderId, "stock recovery"), rollbackContext));
        Value(await rollback.RollbackAsync(new(setup.OrderId, "stock recovery"), rollbackContext));

        await using var verify = database.CreateDbContext();
        var saved = await verify.ShippingOrders.Include(x => x.Items).SingleAsync(x => x.Id == setup.OrderId);
        Assert.Equal(ShippingOrderStatus.Prepared, saved.Status);
        Assert.Null(saved.ShippingLocationId);
        Assert.All(saved.Items, item => Assert.Equal(0m, item.FactQuantity));
        Assert.Equal(100m, (await verify.InventoryBalances.SingleAsync(x => x.Id == setup.StorageBalanceId)).Quantity);
        Assert.Equal(0m, await verify.InventoryBalances.Where(x => x.StorageLocationId == setup.ShippingLocationId).SumAsync(x => x.Quantity));
        Assert.Equal(2, await verify.InventoryMovements.CountAsync(x => x.RecorderId == setup.OrderId));
        Assert.Equal(4, await verify.InventoryTurnovers.CountAsync(x => x.InventoryMovement!.RecorderId == setup.OrderId));
    }

    private async Task<ShippingSetup> CreateShippingSetup()
    {
        var warehouse = new Warehouse { Id = Guid.NewGuid(), Name = "Shipping safety" };
        var sku = new StockKeepingUnit { Id = Guid.NewGuid(), Name = "Shipping SKU" };
        var shippingZone = Zone(warehouse.Id, "SH", ZoneType.Shipping);
        var storageZone = Zone(warehouse.Id, "SS", ZoneType.Storage);
        var shippingLocation = Location(warehouse.Id, shippingZone, 1);
        var storageLocation = Location(warehouse.Id, storageZone, 1);
        var balance = Balance(warehouse.Id, storageLocation.Id, sku.Id, 100m);
        var snapshot = new ShippingOrderImportSnapshot(
            Guid.NewGuid(), false, true, "Shipping", DateTime.UtcNow, warehouse.Id, null,
            ShippingOrderStatus.Prepared, default, null, null, WarehouseOperation.CustomerShipment,
            Guid.NewGuid(), default,
            [new(1, sku.Id, 5m, 5m, ShippingOrderAction.PickUp)],
            [new(1, sku.Id, 5m, Guid.NewGuid(), "CustomerOrder")]);
        var order = Value(ShippingOrder.Create(snapshot, DateTimeOffset.UtcNow));

        await using var seed = database.CreateDbContext();
        seed.AddRange(warehouse, sku, shippingZone, storageZone, shippingLocation, storageLocation, balance, order);
        await seed.SaveChangesAsync();
        return new(order.Id, warehouse.Id, sku.Id, shippingLocation.Id, storageLocation.Id, balance.Id, snapshot);
    }

    private async Task AdjustBalanceAtLocation(Guid locationId, Guid skuId, decimal delta)
    {
        await using var db = database.CreateDbContext();
        var balance = await db.InventoryBalances.SingleAsync(x => x.StorageLocationId == locationId && x.StockKeepingUnitId == skuId);
        Success(balance.Adjust(delta, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
    }

    private static ShippingOrderCommandService ShippingService(
        IDbContextFactory<ApplicationDbContext> factory,
        IShippingOrderSource source,
        IShippingOrderExecutionSink sink) =>
        new(
            new CommandExecutor(factory),
            new InventoryPostingService(NullLogger<InventoryPostingService>.Instance),
            new ShippingOrderSynchronizationService(factory, source, NullLogger<ShippingOrderSynchronizationService>.Instance),
            sink,
            NullLogger<ShippingOrderCommandService>.Instance);

    private sealed record ShippingSetup(
        Guid OrderId,
        Guid WarehouseId,
        Guid SkuId,
        Guid ShippingLocationId,
        Guid StorageLocationId,
        Guid StorageBalanceId,
        ShippingOrderImportSnapshot Snapshot);

    private sealed class MutableShippingSource : IShippingOrderSource
    {
        public int Calls { get; private set; }
        public ShippingOrderImportSnapshot? Snapshot { get; set; }
        public Task<OperationResult<ShippingOrderImportSnapshot>> GetSnapshotAsync(Guid orderId, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult<OperationResult<ShippingOrderImportSnapshot>>(Snapshot!);
        }
    }

    private sealed class MutableShippingSink(MutableShippingSource source) : IShippingOrderExecutionSink
    {
        public int Calls { get; private set; }
        public Task<OperationResult> SetReadyForPickingAsync(Guid orderId, CancellationToken ct) => SuccessResult();
        public Task<OperationResult> UpdateItemsAsync(ShippingOrder order, CancellationToken ct)
        {
            Calls++;
            source.Snapshot = source.Snapshot! with
            {
                Items = order.Items.Select(item => new ShippingOrderItemImportSnapshot(
                    item.LineNumber, item.StockKeepingUnitId, item.FactQuantity, item.FactQuantity,
                    item.FactQuantity > 0 ? ShippingOrderAction.Ship : ShippingOrderAction.DoNotShip)).ToArray()
            };
            return Task.FromResult(OperationResult.Success());
        }
        public Task<OperationResult> SetReadyForShipmentAsync(Guid orderId, CancellationToken ct)
        {
            source.Snapshot = source.Snapshot! with { Status = ShippingOrderStatus.ReadyForShipment, Posted = true };
            return SuccessResult();
        }
        public Task<OperationResult> SetShippedAsync(Guid orderId, CancellationToken ct)
        {
            source.Snapshot = source.Snapshot! with { Status = ShippingOrderStatus.Shipped, Posted = true };
            return SuccessResult();
        }
        private Task<OperationResult> SuccessResult()
        {
            Calls++;
            return Task.FromResult(OperationResult.Success());
        }
    }

    private sealed class FixedShippingSource(ShippingOrderImportSnapshot snapshot) : IShippingOrderSource
    {
        public Task<OperationResult<ShippingOrderImportSnapshot>> GetSnapshotAsync(Guid orderId, CancellationToken ct = default) =>
            Task.FromResult<OperationResult<ShippingOrderImportSnapshot>>(snapshot);
    }

    private sealed class PermissiveShippingSink : IShippingOrderExecutionSink
    {
        public Task<OperationResult> SetReadyForPickingAsync(Guid orderId, CancellationToken ct) => Ok();
        public Task<OperationResult> UpdateItemsAsync(ShippingOrder order, CancellationToken ct) => Ok();
        public Task<OperationResult> SetReadyForShipmentAsync(Guid orderId, CancellationToken ct) => Ok();
        public Task<OperationResult> SetShippedAsync(Guid orderId, CancellationToken ct) => Ok();
        private static Task<OperationResult> Ok() => Task.FromResult(OperationResult.Success());
    }

    private sealed class RejectingShippingSource : IShippingOrderSource
    {
        public Task<OperationResult<ShippingOrderImportSnapshot>> GetSnapshotAsync(Guid orderId, CancellationToken ct = default) =>
            throw new InvalidOperationException("Rollback must not call 1C.");
    }

    private sealed class RejectingShippingSink : IShippingOrderExecutionSink
    {
        public Task<OperationResult> SetReadyForPickingAsync(Guid orderId, CancellationToken ct) => Reject();
        public Task<OperationResult> UpdateItemsAsync(ShippingOrder order, CancellationToken ct) => Reject();
        public Task<OperationResult> SetReadyForShipmentAsync(Guid orderId, CancellationToken ct) => Reject();
        public Task<OperationResult> SetShippedAsync(Guid orderId, CancellationToken ct) => Reject();
        private static Task<OperationResult> Reject() => throw new InvalidOperationException("Rollback must not call 1C.");
    }
}
