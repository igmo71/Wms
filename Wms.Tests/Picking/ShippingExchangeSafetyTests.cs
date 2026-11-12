using Microsoft.EntityFrameworkCore;
using Wms.Application.Authorization;
using Wms.Application.CommandExecution;
using Wms.Application.Inventory;
using Wms.Application.Picking;
using Wms.Application.Replenishment;
using Wms.Common;
using Wms.Data;
using Wms.Domain;
using Wms.Domain.Enums;
using Wms.Tests.Infrastructure;
using static Wms.Tests.Infrastructure.TestSupport;

namespace Wms.Tests.Picking;

[Collection(SqlDatabaseCollection.Name)]
public sealed class ShippingExchangeSafetyTests(SqlDatabase database)
{
    private const string Operator = "exchange-operator";
    private const string Manager = "exchange-manager";

    [Fact]
    public async Task Partial_success_retries_only_failed_document_and_keeps_one_stock_issue()
    {
        var scenario = await CreateAsync(2);
        var failed = scenario.SourceIds[0];
        var completed = scenario.SourceIds[1];
        scenario.Transport.FailNextPost.Add(failed);

        var first = Value(await scenario.Command.ExecuteAsync(scenario.Request));
        Assert.False(first.AllConfirmed);
        Assert.Equal(ShippingExchangeStage.PatchConfirmed,
            first.Documents.Single(x => x.ShippingOrderId == failed).Stage);
        Assert.Equal(ShippingExchangeStage.PostConfirmed,
            first.Documents.Single(x => x.ShippingOrderId == completed).Stage);
        await AssertStockAndAllocationAsync(scenario, 2);

        var retry = scenario.Request with { RequestId = Guid.NewGuid() };
        Assert.True(Value(await scenario.Command.ExecuteAsync(retry)).AllConfirmed);
        Assert.True(Value(await scenario.Command.ExecuteAsync(retry)).AllConfirmed);
        Assert.Equal(1, scenario.Transport.Patches[failed]);
        Assert.Equal(2, scenario.Transport.Posts[failed]);
        Assert.Equal(1, scenario.Transport.Patches[completed]);
        Assert.Equal(1, scenario.Transport.Posts[completed]);
        await AssertStockAndAllocationAsync(scenario, 2);
    }

    [Fact]
    public async Task Lost_post_response_confirms_reached_target_without_second_patch_or_post()
    {
        var scenario = await CreateAsync(1);
        var sourceId = scenario.SourceIds[0];
        scenario.Transport.LoseNextPostResponse.Add(sourceId);

        var first = Value(await scenario.Command.ExecuteAsync(scenario.Request));
        Assert.False(first.AllConfirmed);
        Assert.Equal(ShippingExchangeStage.PatchConfirmed, first.Documents[0].Stage);
        Assert.True(Value(await scenario.Command.ExecuteAsync(
            scenario.Request with { RequestId = Guid.NewGuid() })).AllConfirmed);
        Assert.Equal(1, scenario.Transport.Patches[sourceId]);
        Assert.Equal(1, scenario.Transport.Posts[sourceId]);
        await AssertStockAndAllocationAsync(scenario, 1);
    }

    [Fact]
    public async Task Changed_source_requires_fresh_manager_decision_without_changing_allocation()
    {
        var scenario = await CreateAsync(1);
        var sourceId = scenario.SourceIds[0];
        scenario.Transport.FailNextPost.Add(sourceId);
        Assert.False(Value(await scenario.Command.ExecuteAsync(scenario.Request)).AllConfirmed);
        scenario.Transport.ChangeSource(sourceId, "{\"source\":2}");

        var blocked = Value(await scenario.Command.ExecuteAsync(
            scenario.Request with { RequestId = Guid.NewGuid() }));
        Assert.Equal(ShippingExchangeStage.NeedsReview, blocked.Documents[0].Stage);
        Assert.Equal(1, scenario.Transport.Patches[sourceId]);
        Assert.Equal(1, scenario.Transport.Posts[sourceId]);

        await using (var db = database.CreateDbContext())
        {
            var target = await db.ShippingExchangeTargets.SingleAsync(x =>
                x.PickingAssemblyId == scenario.AssemblyId
                && x.ShippingOrderId == sourceId);
            var approver = new ApproveShippingExchange(scenario.ExchangeStore,
                scenario.Transport, scenario.Receipts, scenario.Executor,
                scenario.Permission, TimeProvider.System);
            Value(await approver.ExecuteAsync(new ApproveShippingExchangeRequest(
                Guid.NewGuid(), scenario.AssemblyId, sourceId,
                target.OperationalRevision, "{\"source\":2}",
                "Проверен новый документ 1С", Manager)));
        }
        Assert.True(Value(await scenario.Command.ExecuteAsync(
            scenario.Request with { RequestId = Guid.NewGuid() })).AllConfirmed);
        Assert.Equal(2, scenario.Transport.Patches[sourceId]);
        await AssertStockAndAllocationAsync(scenario, 1);
    }

    [Fact]
    public async Task Deletion_mark_blocks_writes_and_keeps_closed_stock_fact()
    {
        var scenario = await CreateAsync(1);
        var sourceId = scenario.SourceIds[0];
        scenario.Transport.FailNextPost.Add(sourceId);
        Assert.False(Value(await scenario.Command.ExecuteAsync(scenario.Request)).AllConfirmed);
        scenario.Transport.ChangeDeletionMark(sourceId, true);

        var result = Value(await scenario.Command.ExecuteAsync(
            scenario.Request with { RequestId = Guid.NewGuid() }));
        Assert.Equal(ShippingExchangeStage.NeedsReview, result.Documents[0].Stage);
        Assert.Equal(1, scenario.Transport.Patches[sourceId]);
        Assert.Equal(1, scenario.Transport.Posts[sourceId]);
        await AssertStockAndAllocationAsync(scenario, 1);
    }

    [Fact]
    public async Task Concurrent_retry_does_not_write_one_document_twice()
    {
        var scenario = await CreateAsync(1);
        var sourceId = scenario.SourceIds[0];
        scenario.Transport.PausePatch();
        var firstTask = scenario.Command.ExecuteAsync(scenario.Request);
        await scenario.Transport.PatchEntered.WaitAsync(TimeSpan.FromSeconds(20));
        try
        {
            var other = Value(await scenario.Command.ExecuteAsync(
                scenario.Request with { RequestId = Guid.NewGuid() }));
            Assert.False(other.AllConfirmed);
            Assert.NotNull(other.Documents[0].Error);
        }
        finally { scenario.Transport.ReleasePatch(); }
        Assert.True(Value(await firstTask).AllConfirmed);
        Assert.Equal(1, scenario.Transport.Patches[sourceId]);
        Assert.Equal(1, scenario.Transport.Posts[sourceId]);
        await AssertStockAndAllocationAsync(scenario, 1);
    }

    private async Task AssertStockAndAllocationAsync(Scenario scenario, int documentCount)
    {
        await using var db = database.CreateDbContext();
        Assert.Single(await db.InventoryMovements.Where(x =>
            x.RecorderId == scenario.AssemblyId).ToArrayAsync());
        Assert.Single(await db.PickingClosures.Where(x =>
            x.PickingAssemblyId == scenario.AssemblyId).ToArrayAsync());
        var allocations = await db.PickingAllocations.Where(x =>
            x.PickingAssemblyId == scenario.AssemblyId).ToArrayAsync();
        Assert.Equal(documentCount, allocations.Length);
        Assert.All(allocations, x => Assert.Equal(5m, x.Quantity));
        Assert.Equal(scenario.SourceIds.Order(), allocations.Select(x =>
            x.ShippingOrderId).Order());
    }

    private async Task<Scenario> CreateAsync(int documentCount)
    {
        var warehouse = new Warehouse { Id = Guid.NewGuid(), Name = "Exchange safety" };
        var sku = new StockKeepingUnit { Id = Guid.NewGuid(), Name = "Exchange SKU" };
        var zone = Zone(warehouse.Id, "EXCHANGE", ZoneType.Storage);
        var location = Location(warehouse.Id, zone, 1);
        var sources = Enumerable.Range(0, documentCount).Select(_ =>
            Source(Guid.NewGuid(), warehouse.Id, sku.Id)).ToArray();
        var orders = sources.Select(source =>
        {
            var order = Value(ShippingOrder.Create(Import(source,
                ShippingOrderStatus.Prepared), DateTimeOffset.UtcNow));
            Success(order.ApplyCurrentSource(Import(source,
                ShippingOrderStatus.ReadyForPicking), DateTimeOffset.UtcNow));
            Success(order.RecordSourceCheck(source.RawDocumentJson, null,
                DateTimeOffset.UtcNow));
            return order;
        }).ToArray();
        var assembly = PickingAssembly(warehouse.Id, Operator);
        foreach (var source in sources)
            Value(assembly.AttachSource(source.RefKey, source.RawDocumentJson,
                new Dictionary<Guid, decimal> { [sku.Id] = 5m }, Operator,
                DateTimeOffset.UtcNow));
        var started = DateTimeOffset.UtcNow;
        var leaseId = Guid.NewGuid();
        Success(assembly.FreezeForStart(Operator, started));
        Success(assembly.ClaimStart(leaseId, started, started.AddMinutes(10)));
        foreach (var source in sources)
            Success(assembly.RecordSourceStartResult(leaseId, source.RefKey,
                started, null));
        Success(assembly.CompleteStart(leaseId, started));
        var quantity = 5m * documentCount;
        var entry = Value(PickingEntry.Create(assembly.Id, warehouse.Id,
            location.Id, sku.Id, null, quantity, Operator, DateTimeOffset.UtcNow));
        var reservation = Value(PickingReservation.Create(entry.Id, assembly.Id,
            warehouse.Id, location.Id, sku.Id, null, quantity, Operator,
            DateTimeOffset.UtcNow));
        await using (var db = database.CreateDbContext())
        {
            db.AddRange(warehouse, sku, zone, location, assembly, entry,
                reservation, Balance(warehouse.Id, location.Id, sku.Id, quantity));
            db.AddRange(orders);
            await db.SaveChangesAsync();
        }

        var transport = new MutableTransport(sources);
        var permission = new WarehousePermission(new BothPermission());
        var receipts = new CommandExecutionStore(database);
        var executor = new CommandExecutor(receipts);
        var assemblyStore = new PickingAssemblyStore(database);
        var checker = new CheckPickingClosure(assemblyStore,
            new SynchronizeShippingSource(transport,
                new ShippingSourceStore(database), TimeProvider.System),
            new FixedParser(sources), new AllocatePickingFact(), permission,
            TimeProvider.System);
        var close = new ClosePickingAssembly(executor, assemblyStore, checker,
            new AllocatePickingFact(),
            new PostInventoryMovement.Handler(new InventoryPostingStore()),
            new ReplenishmentStore(database), permission, TimeProvider.System);
        var exchangeStore = new ShippingExchangeStore(database);
        var exchange = new ExchangeShippingDocument(exchangeStore, transport,
            transport, executor, permission, TimeProvider.System);
        var combined = new CloseAndShipPickingAssembly(close, exchange,
            assemblyStore, exchangeStore, receipts, executor, permission,
            TimeProvider.System);
        return new Scenario(assembly.Id, sources.Select(x => x.RefKey).Order().ToArray(),
            transport, combined, exchangeStore, receipts, executor, permission,
            new ClosePickingAssemblyRequest(Guid.NewGuid(), assembly.Id,
                assembly.OperationalRevision, Operator));
    }

    private static ShippingSourceSnapshot Source(Guid sourceId, Guid warehouseId,
        Guid skuId) => new(sourceId, "{\"source\":1}", true, false,
        sourceId.ToString("N"), DateTime.UtcNow, warehouseId, null, null,
        ShippingOrderStatus.ReadyForPicking, null,
        WarehouseOperation.CustomerShipment, Guid.NewGuid(), null,
        PartyType.Partner, null, ShippingOrderQueue.Unknown, null, null,
        [new ShippingSourceItem(1, skuId, "5", 5, "5", 5, null,
            ShippingOrderAction.PickUp, false, false, "{}")], []);

    private static ShippingOrderImportSnapshot Import(ShippingSourceSnapshot source,
        ShippingOrderStatus status) => new(source.RefKey, false, true,
        source.Number, source.Date!.Value, source.WarehouseId!.Value,
        null, status, source.Queue, null, null, source.WarehouseOperation,
        source.ReceiverId!.Value, source.ReceiverType!.Value,
        [new ShippingOrderItemImportSnapshot(1,
            source.Items[0].StockKeepingUnitId!.Value, 5, 5,
            ShippingOrderAction.PickUp)], []);

    private sealed record Scenario(Guid AssemblyId, Guid[] SourceIds,
        MutableTransport Transport, CloseAndShipPickingAssembly Command,
        ShippingExchangeStore ExchangeStore, CommandExecutionStore Receipts,
        CommandExecutor Executor, WarehousePermission Permission,
        ClosePickingAssemblyRequest Request);

    private sealed class FixedParser(ShippingSourceSnapshot[] sources)
        : IShippingStoredSnapshotParser
    {
        public OperationResult<ShippingSourceSnapshot> Parse(Guid refKey,
            string storedDocumentJson) => sources.Single(x => x.RefKey == refKey);

        public OperationResult<bool> UnchangedApartFromStatusAndPosting(
            Guid refKey, string originalDocumentJson,
            ShippingSourceSnapshot current) => true;
    }

    private sealed class BothPermission : IWarehousePermissionStore
    {
        public Task<bool> HasManagerRoleAsync(string userId, CancellationToken ct) =>
            Task.FromResult(userId == Manager || userId == Operator);

        public Task<bool> HasOperatorRoleAsync(string userId, CancellationToken ct) =>
            Task.FromResult(userId == Operator);
    }

    private sealed class MutableTransport(ShippingSourceSnapshot[] sources)
        : IShippingSourceReader, IShippingSourceWriter
    {
        private readonly Dictionary<Guid, ShippingSourceSnapshot> _sources =
            sources.ToDictionary(x => x.RefKey);
        private TaskCompletionSource? _patchEntered;
        private TaskCompletionSource? _releasePatch;
        public Dictionary<Guid, int> Patches { get; } = [];
        public Dictionary<Guid, int> Posts { get; } = [];
        public HashSet<Guid> FailNextPost { get; } = [];
        public HashSet<Guid> LoseNextPostResponse { get; } = [];
        public Task PatchEntered => _patchEntered?.Task ?? Task.CompletedTask;

        public void PausePatch()
        {
            _patchEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _releasePatch = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public void ReleasePatch() => _releasePatch?.TrySetResult();

        public void ChangeSource(Guid sourceId, string json) =>
            _sources[sourceId] = _sources[sourceId] with { RawDocumentJson = json };

        public void ChangeDeletionMark(Guid sourceId, bool marked) =>
            _sources[sourceId] = _sources[sourceId] with { DeletionMark = marked };

        public Task<OperationResult<ShippingSourceSnapshot>> ReadAsync(
            Guid refKey, CancellationToken ct) =>
            Task.FromResult<OperationResult<ShippingSourceSnapshot>>(_sources[refKey]);

        public Task<OperationResult> SetReadyForPickingAsync(Guid sourceId,
            CancellationToken ct) => Task.FromResult(OperationResult.Success());

        public Task<OperationResult> SetPreparedAsync(Guid sourceId,
            CancellationToken ct) => Task.FromResult(OperationResult.Success());

        public OperationResult<bool> TargetMatches(ShippingExchangeTarget target,
            string planSourceSnapshotJson, ShippingSourceSnapshot current) =>
            current.RawDocumentJson == target.TargetDocumentJson;

        public async Task<OperationResult> ApplyTargetAsync(
            ShippingExchangeTarget target, string planSourceSnapshotJson,
            ShippingSourceSnapshot current, CancellationToken ct)
        {
            Patches[target.ShippingOrderId] = Patches.GetValueOrDefault(
                target.ShippingOrderId) + 1;
            _patchEntered?.TrySetResult();
            if (_releasePatch is not null)
                await _releasePatch.Task.WaitAsync(ct);
            _sources[target.ShippingOrderId] = current with
            {
                RawDocumentJson = target.TargetDocumentJson,
                Status = ShippingOrderStatus.Shipped,
                Posted = false
            };
            return OperationResult.Success();
        }

        public Task<OperationResult> PostAsync(Guid sourceId, CancellationToken ct)
        {
            Posts[sourceId] = Posts.GetValueOrDefault(sourceId) + 1;
            if (FailNextPost.Remove(sourceId))
                return Task.FromResult<OperationResult>(
                    OperationError.Failure("1С отклонила проведение."));
            _sources[sourceId] = _sources[sourceId] with { Posted = true };
            return Task.FromResult<OperationResult>(
                LoseNextPostResponse.Remove(sourceId)
                    ? OperationError.Failure("Ответ POST потерян.")
                    : OperationResult.Success());
        }
    }
}
