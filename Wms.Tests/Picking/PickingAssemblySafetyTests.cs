using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Wms.Application.Authorization;
using Wms.Application.CommandExecution;
using Wms.Application.Picking;
using Wms.Common;
using Wms.Data;
using Wms.Domain;
using Wms.Domain.Enums;
using Wms.Tests.Infrastructure;
using static Wms.Tests.Infrastructure.TestSupport;

namespace Wms.Tests.Picking;

[Collection(SqlDatabaseCollection.Name)]
public sealed class PickingAssemblySafetyTests(SqlDatabase database)
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PickingRouteReservation_issues_concurrent_ready_starts_in_priority_order_once(
        bool laterStartHasPriority)
    {
        var warehouse = new Warehouse { Id = Guid.NewGuid(), Name = "Concurrent picking starts" };
        var sku = new StockKeepingUnit { Id = Guid.NewGuid(), Name = "Picking SKU" };
        var zone = Zone(warehouse.Id, "PK", ZoneType.Storage);
        var location = Location(warehouse.Id, zone, 1);
        var first = PickingAssembly(warehouse.Id, "first-picker");
        var second = PickingAssembly(warehouse.Id, "second-picker");
        var now = DateTimeOffset.UtcNow;
        var firstSource = ShippingOrder(Guid.NewGuid(), warehouse.Id, sku.Id, 8, ShippingOrderQueue.CollectByDate);
        var secondSource = ShippingOrder(Guid.NewGuid(), warehouse.Id, sku.Id, 8,
            laterStartHasPriority ? ShippingOrderQueue.LiveQueue : ShippingOrderQueue.CollectByDate);

        MarkReady(first, firstSource.Id, now);
        MarkReady(second, secondSource.Id, now.AddMilliseconds(1));
        await using (var seed = database.CreateDbContext())
        {
            seed.AddRange(warehouse, sku, zone, location, firstSource, secondSource, first, second,
                Value(InventoryBalance.Create(Guid.NewGuid(), warehouse.Id, location.Id, sku.Id, 10, now)));
            await seed.SaveChangesAsync();
        }

        var reader = new MutableReader([]);
        var writer = new MutableWriter(reader, Guid.NewGuid());
        var start = Start(reader, writer);
        var firstRequest = new StartPickingAssemblyRequest(Guid.NewGuid(), first.Id, "first-picker");
        var secondRequest = new StartPickingAssemblyRequest(Guid.NewGuid(), second.Id, "second-picker");
        var completed = await Task.WhenAll(start.ExecuteAsync(firstRequest), start.ExecuteAsync(secondRequest));
        Assert.All(completed, result => Assert.Equal(PickingAssemblyStatus.InProgress, Value(result).Status));
        Assert.Equal(PickingAssemblyStatus.InProgress, Value(await start.ExecuteAsync(firstRequest)).Status);
        Assert.Equal(PickingAssemblyStatus.InProgress, Value(await start.ExecuteAsync(secondRequest)).Status);
        Assert.Empty(writer.PatchCalls);

        await using var verify = database.CreateDbContext();
        var reservations = await verify.PickingRouteReservations
            .Where(x => x.WarehouseId == warehouse.Id).ToDictionaryAsync(x => x.PickingAssemblyId);
        Assert.Equal(2, reservations.Count);
        Assert.Equal(laterStartHasPriority ? 2m : 8m, reservations[first.Id].Quantity);
        Assert.Equal(laterStartHasPriority ? 8m : 2m, reservations[second.Id].Quantity);
        Assert.Equal(2, await verify.CommandReceipts.CountAsync(x =>
            x.CommandType == "picking.assembly.start.allocate"
            && (x.ResultResourceId == first.Id || x.ResultResourceId == second.Id)));
        Assert.False(await verify.PickingEntries.AnyAsync(x => x.WarehouseId == warehouse.Id));

        void MarkReady(PickingAssembly assembly, Guid sourceId, DateTimeOffset readyAt)
        {
            var lease = Guid.NewGuid();
            Value(assembly.AttachSource(sourceId, "{\"version\":1}",
                new Dictionary<Guid, decimal> { [sku.Id] = 8 }, assembly.ResponsibleUserId, now));
            Success(assembly.FreezeForStart(assembly.ResponsibleUserId, now));
            Success(assembly.ClaimStart(lease, now, now.AddMinutes(10)));
            Success(assembly.RecordSourceStartResult(lease, sourceId, readyAt, null));
        }
    }

    [Fact]
    public async Task Concurrent_attach_never_duplicates_source_and_conflicted_request_can_retry()
    {
        var warehouseId = Guid.NewGuid();
        var skuId = Guid.NewGuid();
        var sourceIds = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var assemblies = new[] { PickingAssembly(warehouseId, "operator"),
            PickingAssembly(warehouseId, "operator") };
        var reader = new MutableReader(sourceIds.Select(id => Source(id, warehouseId, skuId, 2)));
        await using (var seed = database.CreateDbContext())
        {
            seed.Add(new Warehouse { Id = warehouseId, Name = "Concurrent picking links" });
            seed.Add(new StockKeepingUnit { Id = skuId, Name = "Picking SKU" });
            foreach (var sourceId in sourceIds)
                seed.Add(ShippingOrder(sourceId, warehouseId, skuId, 2));
            for (var index = 0; index < assemblies.Length; index++)
            {
                Value(assemblies[index].AttachSource(sourceIds[index], "{\"version\":1}",
                    new Dictionary<Guid, decimal> { [skuId] = 2 }, "operator",
                    DateTimeOffset.UtcNow));
                seed.Add(assemblies[index]);
            }
            await seed.SaveChangesAsync();
        }

        var executor = new CommandExecutor(new CommandExecutionStore(
            database.WithInterceptor(new ReceiptSaveBarrier())));
        var attach = new AttachPickingSource(executor, new PickingAssemblyStore(database),
            new SynchronizeShippingSource(reader, new NoOpSourceStore(), TimeProvider.System),
            Permission(), TimeProvider.System);
        var requests = assemblies.Select(x => new AttachPickingSourceRequest(
            Guid.NewGuid(), x.Id, sourceIds[2], "operator")).ToArray();
        var results = await Task.WhenAll(requests.Select(x => attach.ExecuteAsync(x)));

        Assert.All(results, result => Assert.True(result.IsSuccess
            || result.Error?.Type == OperationErrorType.Conflict,
            result.Error?.Message));
        Assert.InRange(results.Count(x => x.IsSuccess), 0, 1);
        if (results.All(x => !x.IsSuccess))
            Value(await attach.ExecuteAsync(requests[0] with { RequestId = Guid.NewGuid() }));
        await using var verify = database.CreateDbContext();
        Assert.Equal(1, await verify.PickingAssemblySourceLinks.CountAsync(x =>
            x.ShippingOrderId == sourceIds[2] && x.DetachedAtUtc == null));
        Assert.Equal(1, await verify.CommandReceipts.CountAsync(x =>
            x.CommandType == "picking.assembly.source.attach"
            && (x.ResultResourceId == assemblies[0].Id
                || x.ResultResourceId == assemblies[1].Id)));
    }

    [Fact]
    public async Task Partial_start_replays_without_writes_then_resumes_only_failed_source_and_keeps_frozen_plan()
    {
        var warehouseId = Guid.NewGuid();
        var skuId = Guid.NewGuid();
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var reader = new MutableReader([
            Source(firstId, warehouseId, skuId, 2),
            Source(secondId, warehouseId, skuId, 3)
        ]);
        var assembly = PickingAssembly(warehouseId, "operator");
        Value(assembly.AttachSource(firstId, "{\"version\":1}",
            new Dictionary<Guid, decimal> { [skuId] = 2 }, "operator",
            DateTimeOffset.UtcNow));
        Value(assembly.AttachSource(secondId, "{\"version\":1}",
            new Dictionary<Guid, decimal> { [skuId] = 3 }, "operator",
            DateTimeOffset.UtcNow));
        await using (var seed = database.CreateDbContext())
        {
            seed.Add(new Warehouse { Id = warehouseId, Name = "Partial picking start" });
            seed.Add(new StockKeepingUnit { Id = skuId, Name = "Picking SKU" });
            seed.Add(ShippingOrder(firstId, warehouseId, skuId, 2));
            seed.Add(ShippingOrder(secondId, warehouseId, skuId, 3));
            seed.Add(assembly);
            await seed.SaveChangesAsync();
        }

        var synchronize = new SynchronizeShippingSource(reader,
            new ShippingSourceStore(database), TimeProvider.System);
        var writer = new MutableWriter(reader, secondId);
        var executor = new CommandExecutor(new CommandExecutionStore(database));
        var start = Start(reader, writer);
        var request = new StartPickingAssemblyRequest(Guid.NewGuid(), assembly.Id, "operator");

        var partial = Value(await start.ExecuteAsync(request));
        Assert.Equal(PickingAssemblyStatus.Preparing, partial.Status);
        Assert.True(partial.Sources.Single(x => x.ShippingOrderId == firstId).Confirmed);
        Assert.False(partial.Sources.Single(x => x.ShippingOrderId == secondId).Confirmed);
        Assert.Equal(1, writer.PatchCalls[firstId]);
        Assert.Equal(1, writer.PatchCalls[secondId]);

        var replay = Value(await start.ExecuteAsync(request));
        Assert.Equal(PickingAssemblyStatus.Preparing, replay.Status);
        Assert.Equal(1, writer.PatchCalls[firstId]);
        Assert.Equal(1, writer.PatchCalls[secondId]);

        reader.Replace(Source(firstId, warehouseId, skuId, 9,
            ShippingOrderStatus.ReadyForPicking) with { RawDocumentJson = "{\"version\":2}" });
        Value(await synchronize.ExecuteAsync(firstId));
        var resumed = Value(await start.ExecuteAsync(request with { RequestId = Guid.NewGuid() }));
        Assert.Equal(PickingAssemblyStatus.InProgress, resumed.Status);
        Assert.Equal(1, writer.PatchCalls[firstId]);
        Assert.Equal(2, writer.PatchCalls[secondId]);

        await using (var verify = database.CreateDbContext())
        {
            Assert.Equal(2, await verify.PickingAssemblyPlanLines.CountAsync(x =>
                x.PickingAssemblyId == assembly.Id));
            Assert.Equal(2m, (await verify.PickingAssemblyPlanLines.SingleAsync(x =>
                x.PickingAssemblyId == assembly.Id && x.ShippingOrderId == firstId)).Quantity);
            Assert.Equal(9m, (await verify.ShippingOrderItems.SingleAsync(x =>
                x.ShippingOrderId == firstId)).PlanQuantity);
        }

        var store = new PickingAssemblyStore(database);
        var current = (await store.ReadAssemblyAsync(assembly.Id, default))!;
        var takeover = new TakeOverPickingAssembly(executor, store, Permission(), TimeProvider.System);
        Value(await takeover.ExecuteAsync(new TakeOverPickingAssemblyRequest(
            Guid.NewGuid(), assembly.Id, current.OperationalRevision, "next-operator")));
        Error(await takeover.ExecuteAsync(new TakeOverPickingAssemblyRequest(
            Guid.NewGuid(), assembly.Id, current.OperationalRevision, "third-operator")),
            OperationErrorType.Conflict);
    }

    private static WarehousePermission Permission() => new(new OperatorPermission());

    private StartPickingAssembly Start(IShippingSourceReader reader, IShippingSourceWriter writer)
    {
        var commandStore = new CommandExecutionStore(database);
        var executor = new CommandExecutor(commandStore);
        var store = new PickingAssemblyStore(database);
        return new StartPickingAssembly(executor, commandStore, store,
            new SynchronizeShippingSource(reader, new ShippingSourceStore(database), TimeProvider.System),
            writer, Permission(), new AllocatePickingStart(executor, store, new StockAvailabilityStore(database),
                new BuildPickingRoute(), TimeProvider.System), TimeProvider.System);
    }

    private static ShippingOrder ShippingOrder(Guid id, Guid warehouseId,
        Guid skuId, decimal quantity, ShippingOrderQueue queue = ShippingOrderQueue.Unknown)
    {
        var order = Value(Wms.Domain.ShippingOrder.Create(new ShippingOrderImportSnapshot(
            id, false, true, "РО 1С", DateTime.UtcNow, warehouseId, null,
            ShippingOrderStatus.Prepared, queue, null, null,
            WarehouseOperation.CustomerShipment, Guid.NewGuid(), PartyType.Partner,
            [new ShippingOrderItemImportSnapshot(1, skuId, quantity, quantity,
                ShippingOrderAction.PickUp)], []), DateTimeOffset.UtcNow));
        Success(order.RecordSourceCheck("{\"version\":1}", null, DateTimeOffset.UtcNow));
        return order;
    }

    private static ShippingSourceSnapshot Source(Guid id, Guid warehouseId,
        Guid skuId, decimal quantity,
        ShippingOrderStatus status = ShippingOrderStatus.Prepared) => new(
            RefKey: id, RawDocumentJson: "{\"version\":1}", Posted: true,
            DeletionMark: false, Number: "РО 1С", Date: DateTime.UtcNow,
            WarehouseId: warehouseId, Comment: null, RawStatus: null,
            Status: status, RawWarehouseOperation: null,
            WarehouseOperation: WarehouseOperation.CustomerShipment,
            ReceiverId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            RawReceiverType: null, ReceiverType: PartyType.Partner,
            RawQueue: null, Queue: ShippingOrderQueue.Unknown,
            PlannedShippingDate: null, DeliveryDirectionId: null,
            Items: [new ShippingSourceItem(1, skuId, quantity.ToString(), quantity,
                quantity.ToString(), quantity, null, ShippingOrderAction.PickUp,
                false, false, "{}")], BasisItems: []);

    private sealed class MutableReader(IEnumerable<ShippingSourceSnapshot> sources)
        : IShippingSourceReader
    {
        private readonly ConcurrentDictionary<Guid, ShippingSourceSnapshot> _sources =
            new(sources.ToDictionary(x => x.RefKey));

        public Task<OperationResult<ShippingSourceSnapshot>> ReadAsync(
            Guid refKey, CancellationToken ct) => Task.FromResult<OperationResult<ShippingSourceSnapshot>>(
                _sources[refKey]);

        public void Replace(ShippingSourceSnapshot source) => _sources[source.RefKey] = source;
    }

    private sealed class MutableWriter(MutableReader reader, Guid failOnceId)
        : IShippingSourceWriter
    {
        private bool _failed;
        public Dictionary<Guid, int> PatchCalls { get; } = [];

        public async Task<OperationResult> SetReadyForPickingAsync(Guid sourceId,
            CancellationToken ct)
        {
            PatchCalls[sourceId] = PatchCalls.GetValueOrDefault(sourceId) + 1;
            if (sourceId == failOnceId && !_failed)
            {
                _failed = true;
                return OperationError.Failure("1С временно не приняла PATCH.");
            }
            var source = Value(await reader.ReadAsync(sourceId, ct));
            reader.Replace(source with { Status = ShippingOrderStatus.ReadyForPicking });
            return OperationResult.Success();
        }

        public Task<OperationResult> PostAsync(Guid sourceId, CancellationToken ct) =>
            Task.FromResult(OperationResult.Success());

        public Task<OperationResult> SetPreparedAsync(Guid sourceId,
            CancellationToken ct) => throw new NotSupportedException();

        public Task<OperationResult> ApplyTargetAsync(ShippingExchangeTarget target,
            string planSourceSnapshotJson, ShippingSourceSnapshot current,
            CancellationToken ct) => throw new NotSupportedException();

        public OperationResult<bool> TargetMatches(ShippingExchangeTarget target,
            string planSourceSnapshotJson, ShippingSourceSnapshot current) =>
            throw new NotSupportedException();

        public OperationResult ValidateTarget(ShippingExchangeTarget target,
            string planSourceSnapshotJson) => throw new NotSupportedException();
    }

    private sealed class NoOpSourceStore : IShippingSourceStore
    {
        public Task<OperationResult<Guid>> SaveAsync(ShippingOrderImportSnapshot source,
            string rawDocumentJson, IReadOnlyDictionary<Guid, decimal> planBySku,
            DateTimeOffset checkedAtUtc, CancellationToken ct) =>
            Task.FromResult<OperationResult<Guid>>(source.Id);

        public Task RecordFailureAsync(Guid refKey, string? rawDocumentJson,
            string error, DateTimeOffset checkedAtUtc, CancellationToken ct) =>
            Task.CompletedTask;
    }

    private sealed class OperatorPermission : IWarehousePermissionStore
    {
        public Task<bool> HasManagerRoleAsync(string userId, CancellationToken ct) =>
            Task.FromResult(false);
        public Task<bool> HasOperatorRoleAsync(string userId, CancellationToken ct) =>
            Task.FromResult(true);
    }
}
