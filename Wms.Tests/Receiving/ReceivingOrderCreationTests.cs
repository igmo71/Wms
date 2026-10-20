using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Text.Json;
using Wms.Application.Authorization;
using Wms.Application.CommandExecution;
using Wms.Application.Receiving;
using Wms.Common;
using Wms.Data;
using Wms.Domain;
using Wms.Domain.Enums;
using Wms.Integration.OneS;
using Wms.Tests.Infrastructure;
using static Wms.Tests.Infrastructure.TestSupport;

namespace Wms.Tests.Receiving;

[Collection(SqlDatabaseCollection.Name)]
public sealed class ReceivingOrderCreationTests(SqlDatabase database)
{
    [Fact]
    public async Task Concurrent_creation_uses_adjacent_numbers_and_replay_keeps_original_number()
    {
        var warehouseId = Guid.NewGuid();
        var skuId = Guid.NewGuid();
        var firstKey = Guid.NewGuid();
        var secondKey = Guid.NewGuid();
        var createdAtUtc = new DateTimeOffset(2048, 1, 2, 12, 34, 56, TimeSpan.Zero);
        await using (var seed = database.CreateDbContext())
        {
            seed.Warehouses.Add(new Warehouse { Id = warehouseId, Name = "Number collision" });
            seed.StockKeepingUnits.Add(new StockKeepingUnit { Id = skuId, Name = "Number SKU" });
            await seed.SaveChangesAsync();
        }

        var responses = new Dictionary<Guid, string>
        {
            [firstKey] = Source(firstKey, warehouseId, $$"""
                [{"LineNumber":"1","Номенклатура_Key":"{{skuId}}","Количество":1}]
                """),
            [secondKey] = Source(secondKey, warehouseId, $$"""
                [{"LineNumber":"1","Номенклатура_Key":"{{skuId}}","Количество":2}]
                """)
        };
        var parser = new ReceivingSourceParser();
        var synchronization = new SynchronizeReceivingSource(
            new StubReader(responses), parser,
            new ReceivingSourceStore(database, parser), TimeProvider.System);
        Assert.True((await synchronization.ExecuteAsync(firstKey)).IsSuccess);
        Assert.True((await synchronization.ExecuteAsync(secondKey)).IsSuccess);

        var store = new ConcurrentCreateStore(new ReceivingOrderStore(database));
        var create = new CreateReceivingOrder(store, parser,
            new FixedTimeProvider(createdAtUtc));
        var firstRequest = new CreateReceivingOrderRequest(
            Guid.NewGuid(), [firstKey], "manager");
        var secondRequest = new CreateReceivingOrderRequest(
            Guid.NewGuid(), [secondKey], "manager");
        var results = await Task.WhenAll(create.ExecuteAsync(firstRequest),
            create.ExecuteAsync(secondRequest));
        Assert.All(results, result => Assert.True(result.IsSuccess, result.Error?.Message));
        var ids = results.Select(result => Value(result)).ToArray();
        Assert.Equal(2, ids.Distinct().Count());

        await using var verify = database.CreateDbContext();
        var orders = await verify.ReceivingOrders.AsNoTracking()
            .Where(order => ids.Contains(order.Id))
            .OrderBy(order => order.Number)
            .Select(order => new { order.Id, order.Number, order.CreatedAtUtc })
            .ToArrayAsync();
        Assert.Equal(2, orders.Length);
        Assert.All(orders, order => Assert.Equal(createdAtUtc, order.CreatedAtUtc));
        var localSecond = createdAtUtc.LocalDateTime;
        Assert.Equal(new[]
        {
            localSecond.ToString("yyMMdd-HHmmss", CultureInfo.InvariantCulture),
            localSecond.AddSeconds(1).ToString("yyMMdd-HHmmss", CultureInfo.InvariantCulture)
        }, orders.Select(order => order.Number).ToArray());

        Assert.Equal(ids[0], Value(await create.ExecuteAsync(firstRequest)));
        Assert.Equal(ids[1], Value(await create.ExecuteAsync(secondRequest)));
        var replayedOrders = await verify.ReceivingOrders.AsNoTracking()
            .Where(order => ids.Contains(order.Id))
            .OrderBy(order => order.Number)
            .Select(order => new { order.Id, order.Number, order.CreatedAtUtc })
            .ToArrayAsync();
        Assert.Equal(orders, replayedOrders);
        Assert.Equal(2, await verify.ReceivingOrders.CountAsync(order => ids.Contains(order.Id)));
        Assert.Equal(2, await verify.WarehouseAuditEvents.CountAsync(audit =>
            audit.EventType == "receiving.order.created"
            && audit.ReceivingOrderId != null && ids.Contains(audit.ReceivingOrderId.Value)));
        Assert.Equal(2, await verify.CommandReceipts.CountAsync(receipt =>
            receipt.CommandType == "receiving.order.create"
            && ids.Contains(receipt.ResultResourceId)));
    }

    [Fact]
    public async Task Number_migration_adds_required_unique_number_to_empty_database()
    {
        var emptyDatabase = new SqlDatabase();
        try
        {
            await using (var before = emptyDatabase.CreateDbContext())
            {
                await before.Database.GetService<IMigrator>()
                    .MigrateAsync("20261019094514_RemoveLegacyReceiving");
                await before.Database.GetService<IMigrator>().MigrateAsync();
            }

            await using var after = emptyDatabase.CreateDbContext();
            Assert.Null(await after.ReceivingOrders.AsNoTracking().FirstOrDefaultAsync());
            var nullable = await after.Database.SqlQueryRaw<int>("""
                SELECT CAST([is_nullable] AS int) AS [Value]
                FROM sys.columns
                WHERE [object_id] = OBJECT_ID('ReceivingOrders') AND [name] = 'Number'
                """).SingleAsync();
            Assert.Equal(0, nullable);
            var uniqueIndexCount = await after.Database.SqlQueryRaw<int>("""
                SELECT COUNT(*) AS [Value]
                FROM sys.indexes
                WHERE [object_id] = OBJECT_ID('ReceivingOrders')
                    AND [name] = 'IX_ReceivingOrders_Number' AND [is_unique] = 1
                """).SingleAsync();
            Assert.Equal(1, uniqueIndexCount);
        }
        finally
        {
            await emptyDatabase.DisposeAsync();
        }
    }

    [Fact]
    public async Task Completing_and_reopening_keeps_old_cycle_snapshot_and_posts_no_stock()
    {
        var warehouseId = Guid.NewGuid();
        var skuId = Guid.NewGuid();
        var sourceKey = Guid.NewGuid();
        var zone = Zone(warehouseId, "RC", ZoneType.Receiving);
        var firstLocation = Location(warehouseId, zone, 1);
        var secondLocation = Location(warehouseId, zone, 2);
        await using (var seed = database.CreateDbContext())
        {
            seed.Warehouses.Add(new Warehouse { Id = warehouseId, Name = "Receiving cycles" });
            seed.Zones.Add(zone);
            seed.StorageLocations.AddRange(firstLocation, secondLocation);
            seed.StockKeepingUnits.Add(new StockKeepingUnit { Id = skuId, Name = "Cycle SKU" });
            await seed.SaveChangesAsync();
        }

        var responses = new Dictionary<Guid, string>
        {
            [sourceKey] = Source(sourceKey, warehouseId, $$"""
                [{"LineNumber":"1","Номенклатура_Key":"{{skuId}}","Количество":2}]
                """)
        };
        var parser = new ReceivingSourceParser();
        var synchronize = new SynchronizeReceivingSource(
            new StubReader(responses), parser,
            new ReceivingSourceStore(database, parser), TimeProvider.System);
        Assert.True((await synchronize.ExecuteAsync(sourceKey)).IsSuccess);
        var orderId = Value(await new CreateReceivingOrder(
            new ReceivingOrderStore(database), parser, TimeProvider.System)
            .ExecuteAsync(new CreateReceivingOrderRequest(Guid.NewGuid(), [sourceKey], "manager")));
        var executor = new CommandExecutor(new CommandExecutionStore(database));
        var store = new ReceivingWorkStore(database);
        var permission = new WarehousePermission(new ManagerPermission());
        var join = new JoinReceivingCycle(
            executor, store, synchronize, parser, permission, TimeProvider.System);
        var zero = new ConfirmZeroReceived(executor, store, permission, TimeProvider.System);
        var complete = new CompleteReceivingOrder(
            executor, store, synchronize, permission, TimeProvider.System);
        var reopen = new ReopenReceivingOrder(
            executor, store, permission, TimeProvider.System);

        Assert.Equal(orderId, Value(await join.ExecuteAsync(new JoinReceivingCycleRequest(
            Guid.NewGuid(), orderId, firstLocation.Id, "manager"))));
        Assert.Equal(skuId, Value(await zero.ExecuteAsync(new ConfirmZeroReceivedRequest(
            Guid.NewGuid(), orderId, skuId, "manager"))));
        var firstComplete = new CompleteReceivingOrderRequest(
            Guid.NewGuid(), orderId, "manager");
        Assert.Equal(orderId, Value(await complete.ExecuteAsync(firstComplete)));

        responses[sourceKey] = Source(sourceKey, warehouseId, $$"""
            [{"LineNumber":"1","Номенклатура_Key":"{{skuId}}","Количество":3}]
            """);
        Assert.True((await synchronize.ExecuteAsync(sourceKey)).IsSuccess);
        var reopenRequest = new ReopenReceivingOrderRequest(
            Guid.NewGuid(), orderId, "manager", true);
        Assert.Equal(orderId, Value(await reopen.ExecuteAsync(reopenRequest)));
        Assert.Equal(orderId, Value(await reopen.ExecuteAsync(reopenRequest)));
        Assert.Equal(orderId, Value(await join.ExecuteAsync(new JoinReceivingCycleRequest(
            Guid.NewGuid(), orderId, secondLocation.Id, "manager"))));
        Assert.Equal(orderId, Value(await complete.ExecuteAsync(new CompleteReceivingOrderRequest(
            Guid.NewGuid(), orderId, "manager"))));
        Assert.Equal(orderId, Value(await complete.ExecuteAsync(firstComplete)));

        await using var verify = database.CreateDbContext();
        var cycles = await verify.ReceivingCycles.Include(x => x.SourceVersions)
            .Where(x => x.ReceivingOrderId == orderId)
            .OrderBy(x => x.Number).ToArrayAsync();
        Assert.Equal(2, cycles.Length);
        Assert.Equal(firstLocation.Id, cycles[0].ReceivingLocationId);
        Assert.Equal(secondLocation.Id, cycles[1].ReceivingLocationId);
        Assert.Equal(2m, JsonDocument.Parse(cycles[0].PlanSnapshotJson!)
            .RootElement[0].GetProperty("Quantity").GetDecimal());
        Assert.Equal(3m, JsonDocument.Parse(cycles[1].PlanSnapshotJson!)
            .RootElement[0].GetProperty("Quantity").GetDecimal());
        Assert.Equal(0m, JsonDocument.Parse(cycles[0].FactSnapshotJson!)
            .RootElement[0].GetProperty("Quantity").GetDecimal());
        Assert.Single(cycles[0].SourceVersions);
        Assert.Single(cycles[1].SourceVersions);
        Assert.NotEqual(cycles[0].SourceVersions.Single().ReceivingSourceVersionId,
            cycles[1].SourceVersions.Single().ReceivingSourceVersionId);
        Assert.Equal(2, await verify.ReceivingOrderParticipants.CountAsync(x =>
            x.ReceivingOrderId == orderId));
        Assert.False(await verify.InventoryMovements.AnyAsync(x => x.RecorderId == orderId));
    }

    [Fact]
    public async Task Changing_sources_rebuilds_plan_and_last_detach_keeps_order_visible()
    {
        var warehouseId = Guid.NewGuid();
        var skuId = Guid.NewGuid();
        var firstKey = Guid.NewGuid();
        var secondKey = Guid.NewGuid();
        await using (var seed = database.CreateDbContext())
        {
            seed.Warehouses.Add(new Warehouse { Id = warehouseId, Name = "Changing sources" });
            seed.StockKeepingUnits.Add(new StockKeepingUnit { Id = skuId, Name = "Source SKU" });
            await seed.SaveChangesAsync();
        }

        var responses = new Dictionary<Guid, string>
        {
            [firstKey] = Source(firstKey, warehouseId, $$"""
                [{"LineNumber":"1","Номенклатура_Key":"{{skuId}}","Количество":2}]
                """),
            [secondKey] = Source(secondKey, warehouseId, $$"""
                [{"LineNumber":"1","Номенклатура_Key":"{{skuId}}","Количество":3}]
                """)
        };
        var parser = new ReceivingSourceParser();
        var synchronization = new SynchronizeReceivingSource(
            new StubReader(responses), parser,
            new ReceivingSourceStore(database, parser), TimeProvider.System);
        Assert.True((await synchronization.ExecuteAsync(firstKey)).IsSuccess);
        var orderId = Value(await new CreateReceivingOrder(
            new ReceivingOrderStore(database), parser, TimeProvider.System)
            .ExecuteAsync(new CreateReceivingOrderRequest(Guid.NewGuid(), [firstKey], "manager")));
        var query = new ReceivingWorkQueryStore(database);
        var sourceChangeStore = new ReceivingOrderSourceChangeStore(database);
        var permission = new WarehousePermission(new ManagerPermission());
        var preview = new PreviewReceivingOrderSourceChange(sourceChangeStore,
            parser, synchronization, new ReceivingReferenceNameStore(database), permission);
        var change = new ChangeReceivingOrderSources(
            new CommandExecutor(new CommandExecutionStore(database)),
            sourceChangeStore, query, parser, synchronization, permission, TimeProvider.System);

        async Task ChangeAsync(Guid key, ReceivingSourceChangeKind kind)
        {
            var state = Value(await preview.ExecuteAsync(
                new PreviewReceivingOrderSourceChangeRequest(orderId, key, kind, "manager")));
            var result = await change.ExecuteAsync(new ChangeReceivingOrderSourcesRequest(
                Guid.NewGuid(), orderId, key, "manager", kind,
                ReceivingSourceChangeChannel.Web, state.StateToken));
            Assert.Equal(orderId, Value(result));
        }

        await ChangeAsync(secondKey, ReceivingSourceChangeKind.Attach);
        await using (var attached = database.CreateDbContext())
            Assert.Equal(5m, (await attached.ReceivingOrderItems.SingleAsync(x =>
                x.ReceivingOrderId == orderId && x.StockKeepingUnitId == skuId))
                .CurrentPlanQuantity);

        await ChangeAsync(firstKey, ReceivingSourceChangeKind.Detach);
        await ChangeAsync(secondKey, ReceivingSourceChangeKind.Detach);
        await using var verify = database.CreateDbContext();
        var item = await verify.ReceivingOrderItems.SingleAsync(x =>
            x.ReceivingOrderId == orderId && x.StockKeepingUnitId == skuId);
        Assert.Equal(0m, item.CurrentPlanQuantity);
        Assert.Null(item.FactQuantity);
        Assert.Empty((await query.GetOrderAsync(orderId, "manager", default))!.SourceRefKeys);
        Assert.Equal(3, await verify.CommandReceipts.CountAsync(x =>
            x.CommandType == "receiving.order.change-sources"));
        Assert.Equal(3, await verify.WarehouseAuditEvents.CountAsync(x =>
            x.ReceivingOrderId == orderId && x.CommandType == "receiving.order.change-sources"));
    }

    [Fact]
    public async Task Web_source_change_rejects_stale_version_changed_links_and_operator()
    {
        var warehouseId = Guid.NewGuid();
        var skuId = Guid.NewGuid();
        var firstKey = Guid.NewGuid();
        var secondKey = Guid.NewGuid();
        var thirdKey = Guid.NewGuid();
        await using (var seed = database.CreateDbContext())
        {
            seed.AddRange(new Warehouse { Id = warehouseId, Name = "Source preview" },
                new StockKeepingUnit { Id = skuId, Name = "Source SKU" });
            await seed.SaveChangesAsync();
        }

        string SourceWithQuantity(Guid key, int quantity) => Source(key, warehouseId, $$"""
            [{"LineNumber":"1","Номенклатура_Key":"{{skuId}}","Количество":{{quantity}}}]
            """);
        var responses = new Dictionary<Guid, string>
        {
            [firstKey] = SourceWithQuantity(firstKey, 2),
            [secondKey] = SourceWithQuantity(secondKey, 3),
            [thirdKey] = SourceWithQuantity(thirdKey, 5)
        };
        var parser = new ReceivingSourceParser();
        var synchronize = new SynchronizeReceivingSource(new StubReader(responses), parser,
            new ReceivingSourceStore(database, parser), TimeProvider.System);
        Assert.True((await synchronize.ExecuteAsync(firstKey)).IsSuccess);
        var orderId = Value(await new CreateReceivingOrder(
            new ReceivingOrderStore(database), parser, TimeProvider.System)
            .ExecuteAsync(new CreateReceivingOrderRequest(Guid.NewGuid(), [firstKey], "manager")));
        var store = new ReceivingOrderSourceChangeStore(database);
        var permission = new WarehousePermission(new ManagerAndOperatorPermission());
        var preview = new PreviewReceivingOrderSourceChange(store, parser, synchronize,
            new ReceivingReferenceNameStore(database), permission);
        var change = new ChangeReceivingOrderSources(
            new CommandExecutor(new CommandExecutionStore(database)), store,
            new ReceivingWorkQueryStore(database), parser, synchronize, permission,
            TimeProvider.System);

        async Task<string> TokenAsync(Guid key, ReceivingSourceChangeKind kind) =>
            Value(await preview.ExecuteAsync(new PreviewReceivingOrderSourceChangeRequest(
                orderId, key, kind, "manager"))).StateToken;

        var staleVersionToken = await TokenAsync(secondKey, ReceivingSourceChangeKind.Attach);
        responses[secondKey] = SourceWithQuantity(secondKey, 4);
        Assert.True((await synchronize.ExecuteAsync(secondKey)).IsSuccess);
        Error(await change.ExecuteAsync(new ChangeReceivingOrderSourcesRequest(
            Guid.NewGuid(), orderId, secondKey, "manager", ReceivingSourceChangeKind.Attach,
            ReceivingSourceChangeChannel.Web, staleVersionToken)), OperationErrorType.Conflict);

        var currentToken = await TokenAsync(secondKey, ReceivingSourceChangeKind.Attach);
        Error(await change.ExecuteAsync(new ChangeReceivingOrderSourcesRequest(
            Guid.NewGuid(), orderId, secondKey, "operator", ReceivingSourceChangeKind.Attach,
            ReceivingSourceChangeChannel.Web, currentToken)), OperationErrorType.Conflict);
        Assert.Equal(orderId, Value(await change.ExecuteAsync(new ChangeReceivingOrderSourcesRequest(
            Guid.NewGuid(), orderId, secondKey, "manager", ReceivingSourceChangeKind.Attach,
            ReceivingSourceChangeChannel.Web, currentToken))));

        var staleLinkToken = await TokenAsync(firstKey, ReceivingSourceChangeKind.Detach);
        var thirdToken = await TokenAsync(thirdKey, ReceivingSourceChangeKind.Attach);
        Assert.Equal(orderId, Value(await change.ExecuteAsync(new ChangeReceivingOrderSourcesRequest(
            Guid.NewGuid(), orderId, thirdKey, "manager", ReceivingSourceChangeKind.Attach,
            ReceivingSourceChangeChannel.Web, thirdToken))));
        Error(await change.ExecuteAsync(new ChangeReceivingOrderSourcesRequest(
            Guid.NewGuid(), orderId, firstKey, "manager", ReceivingSourceChangeKind.Detach,
            ReceivingSourceChangeChannel.Web, staleLinkToken)), OperationErrorType.Conflict);

        await using var verify = database.CreateDbContext();
        Assert.Equal(11m, (await verify.ReceivingOrderItems.SingleAsync(x =>
            x.ReceivingOrderId == orderId && x.StockKeepingUnitId == skuId))
            .CurrentPlanQuantity);
        Assert.Equal(3, await verify.ReceivingOrderSourceLinks.CountAsync(x =>
            x.ReceivingOrderId == orderId && x.DetachedAtUtc == null));
        Assert.Equal(2, await verify.CommandReceipts.CountAsync(x =>
            x.CommandType == "receiving.order.change-sources" && x.ResultResourceId == orderId));
        Assert.Equal(2, await verify.WarehouseAuditEvents.CountAsync(x =>
            x.ReceivingOrderId == orderId && x.CommandType == "receiving.order.change-sources"));
    }

    [Fact]
    public async Task Manager_source_changes_after_posting_and_completion_preserve_fact_and_history_on_replay()
    {
        var warehouseId = Guid.NewGuid();
        var skuId = Guid.NewGuid();
        var firstKey = Guid.NewGuid();
        var secondKey = Guid.NewGuid();
        var zone = Zone(warehouseId, "RC", ZoneType.Receiving);
        var location = Location(warehouseId, zone, 1);
        var batch = Value(LicensePlateNumberBatch.Issue(1, "manager", DateTimeOffset.UtcNow));
        var lpnId = batch.Labels.Single().Id;
        await using (var seed = database.CreateDbContext())
        {
            seed.AddRange(new Warehouse { Id = warehouseId, Name = "Posted source change" },
                new StockKeepingUnit { Id = skuId, Name = "Source SKU" },
                zone, location, batch);
            await seed.SaveChangesAsync();
        }

        string SourceWithQuantity(Guid key, int quantity) => Source(key, warehouseId, $$"""
            [{"LineNumber":"1","Номенклатура_Key":"{{skuId}}","Количество":{{quantity}}}]
            """);
        var responses = new Dictionary<Guid, string>
        {
            [firstKey] = SourceWithQuantity(firstKey, 2),
            [secondKey] = SourceWithQuantity(secondKey, 3)
        };
        var parser = new ReceivingSourceParser();
        var synchronize = new SynchronizeReceivingSource(new StubReader(responses), parser,
            new ReceivingSourceStore(database, parser), TimeProvider.System);
        Assert.True((await synchronize.ExecuteAsync(firstKey)).IsSuccess);
        var orderId = Value(await new CreateReceivingOrder(
            new ReceivingOrderStore(database), parser, TimeProvider.System)
            .ExecuteAsync(new CreateReceivingOrderRequest(Guid.NewGuid(), [firstKey], "manager")));

        await using (var prepare = database.CreateDbContext())
        {
            var order = await prepare.ReceivingOrders
                .Include(x => x.Cycles).Include(x => x.Participants)
                .Include(x => x.Items).Include(x => x.LicensePlateNumbers)
                .ThenInclude(x => x.Items).SingleAsync(x => x.Id == orderId);
            var lpn = await prepare.LicensePlateNumbers.Include(x => x.Items)
                .SingleAsync(x => x.Id == lpnId);
            var now = DateTimeOffset.UtcNow;
            Value(order.JoinCurrentCycle(location.Id, "manager", now));
            prepare.ReceivingOrderParticipants.Add(Assert.Single(order.Participants));
            Assert.True(Value(order.OpenReceivingLpn(lpn, "manager", now)));
            Success(order.EditReceivingLpn(lpn, skuId, null, 1m, "manager", now, false));
            prepare.LicensePlateNumberItems.Add(Assert.Single(lpn.Items));
            await prepare.SaveChangesAsync();
        }

        var executor = new CommandExecutor(new CommandExecutionStore(database));
        var workStore = new ReceivingWorkStore(database);
        var permission = new WarehousePermission(new ManagerPermission());
        var close = new CloseReceivingLpn(executor, workStore, synchronize, parser,
            new Wms.Application.Inventory.PostInventoryMovement.Handler(new InventoryPostingStore()),
            permission, TimeProvider.System);
        Assert.Equal(2m, Value(await close.ExecuteAsync(new CloseReceivingLpnCommand(
            Guid.NewGuid(), orderId, lpnId, "manager"))).UsedPlan);

        var store = new ReceivingOrderSourceChangeStore(database);
        var preview = new PreviewReceivingOrderSourceChange(store, parser, synchronize,
            new ReceivingReferenceNameStore(database), permission);
        var change = new ChangeReceivingOrderSources(executor, store,
            new ReceivingWorkQueryStore(database), parser, synchronize, permission,
            TimeProvider.System);
        var attachToken = Value(await preview.ExecuteAsync(
            new PreviewReceivingOrderSourceChangeRequest(orderId, secondKey,
                ReceivingSourceChangeKind.Attach, "manager"))).StateToken;
        var attach = new ChangeReceivingOrderSourcesRequest(Guid.NewGuid(), orderId, secondKey,
            "manager", ReceivingSourceChangeKind.Attach, ReceivingSourceChangeChannel.Web,
            attachToken);
        Assert.Equal(orderId, Value(await change.ExecuteAsync(attach)));
        Assert.Equal(orderId, Value(await change.ExecuteAsync(attach)));

        var complete = new CompleteReceivingOrder(executor, workStore, synchronize,
            permission, TimeProvider.System);
        Assert.Equal(orderId, Value(await complete.ExecuteAsync(
            new CompleteReceivingOrderRequest(Guid.NewGuid(), orderId, "manager"))));
        var detachToken = Value(await preview.ExecuteAsync(
            new PreviewReceivingOrderSourceChangeRequest(orderId, firstKey,
                ReceivingSourceChangeKind.Detach, "manager"))).StateToken;
        var detach = new ChangeReceivingOrderSourcesRequest(Guid.NewGuid(), orderId, firstKey,
            "manager", ReceivingSourceChangeKind.Detach, ReceivingSourceChangeChannel.Web,
            detachToken);
        Assert.Equal(orderId, Value(await change.ExecuteAsync(detach)));
        Assert.Equal(orderId, Value(await change.ExecuteAsync(detach)));

        await using var verify = database.CreateDbContext();
        var item = await verify.ReceivingOrderItems.SingleAsync(x =>
            x.ReceivingOrderId == orderId && x.StockKeepingUnitId == skuId);
        Assert.Equal(3m, item.CurrentPlanQuantity);
        Assert.Equal(1m, item.FactQuantity);
        Assert.Equal(ReceivingOrderStatus.Received,
            (await verify.ReceivingOrders.SingleAsync(x => x.Id == orderId)).Status);
        var lpnAfter = await verify.LicensePlateNumbers.Include(x => x.Items)
            .SingleAsync(x => x.Id == lpnId);
        Assert.Equal(LicensePlateNumberStatus.Closed, lpnAfter.Status);
        Assert.Equal(1m, Assert.Single(lpnAfter.Items).Quantity);
        Assert.Equal(1, await verify.InventoryMovements.CountAsync(x =>
            x.LicensePlateNumberId == lpnId));
        Assert.Equal(1m, (await verify.InventoryBalances.SingleAsync(x =>
            x.StorageLocationId == location.Id && x.StockKeepingUnitId == skuId)).Quantity);
        var cycle = await verify.ReceivingCycles.Include(x => x.SourceVersions)
            .SingleAsync(x => x.ReceivingOrderId == orderId);
        Assert.Equal(5m, JsonDocument.Parse(cycle.PlanSnapshotJson!).RootElement[0]
            .GetProperty("Quantity").GetDecimal());
        Assert.Equal(1m, JsonDocument.Parse(cycle.FactSnapshotJson!).RootElement[0]
            .GetProperty("Quantity").GetDecimal());
        Assert.Equal(2, cycle.SourceVersions.Count);
        var links = await verify.ReceivingOrderSourceLinks
            .Where(x => x.ReceivingOrderId == orderId).ToArrayAsync();
        Assert.Equal(2, links.Length);
        Assert.Single(links, x => x.DetachedAtUtc is not null);
        Assert.Single(links, x => x.DetachedAtUtc is null);
        Assert.Equal(2, await verify.CommandReceipts.CountAsync(x =>
            x.CommandType == "receiving.order.change-sources" && x.ResultResourceId == orderId));
        Assert.Equal(2, await verify.WarehouseAuditEvents.CountAsync(x =>
            x.ReceivingOrderId == orderId && x.CommandType == "receiving.order.change-sources"));
    }

    [Fact]
    public async Task Several_sources_create_one_order_with_distinct_links_and_aggregated_sku_plan()
    {
        var warehouseId = Guid.NewGuid();
        var otherWarehouseId = Guid.NewGuid();
        var skuId = Guid.NewGuid();
        var uncertainSkuId = Guid.NewGuid();
        var firstKey = Guid.NewGuid();
        var secondKey = Guid.NewGuid();
        var unpostedKey = Guid.NewGuid();
        var otherKey = Guid.NewGuid();
        await using (var seed = database.CreateDbContext())
        {
            seed.Warehouses.AddRange(
                new Warehouse { Id = warehouseId, Name = "Receiving creation" },
                new Warehouse { Id = otherWarehouseId, Name = "Other warehouse" });
            seed.StockKeepingUnits.AddRange(
                new StockKeepingUnit { Id = skuId, Name = "Repeated SKU" },
                new StockKeepingUnit { Id = uncertainSkuId, Name = "Uncertain SKU" });
            await seed.SaveChangesAsync();
        }

        var responses = new Dictionary<Guid, string>
        {
            [firstKey] = Source(firstKey, warehouseId, $$"""
                [{"LineNumber":"1","Номенклатура_Key":"{{skuId}}","Количество":2},
                 {"LineNumber":"2","Номенклатура_Key":"{{skuId}}","Количество":3},
                 {"LineNumber":"3","Номенклатура_Key":"{{uncertainSkuId}}","Количество":"unsupported"}]
                """),
            [secondKey] = Source(secondKey, warehouseId, $$"""
                [{"LineNumber":"1","Номенклатура_Key":"{{skuId}}","Количество":4},
                 {"LineNumber":"2","Номенклатура_Key":"{{uncertainSkuId}}","Количество":7}]
                """),
            [unpostedKey] = Source(unpostedKey, warehouseId, $$"""
                [{"LineNumber":"1","Номенклатура_Key":"{{skuId}}","Количество":100}]
                """, posted: false),
            [otherKey] = Source(otherKey, otherWarehouseId, "[]")
        };
        var parser = new ReceivingSourceParser();
        var synchronization = new SynchronizeReceivingSource(
            new StubReader(responses), parser, new ReceivingSourceStore(database, parser), TimeProvider.System);
        foreach (var key in responses.Keys)
            Assert.True((await synchronization.ExecuteAsync(key)).IsSuccess);

        var store = new ReceivingOrderStore(database);
        var list = new GetReceivingSources(store, parser, new ReceivingReferenceNameStore(database));
        var available = (await list.ListUnlinkedAsync())
            .Where(x => responses.ContainsKey(x.RefKey)).ToList();
        Assert.Equal(4, available.Count);
        Assert.Equal(0m, available.Single(x => x.RefKey == unpostedKey).PlanBySku[skuId]);
        var create = new CreateReceivingOrder(store, parser, TimeProvider.System);
        var wrongWarehouse = await create.ExecuteAsync(new CreateReceivingOrderRequest(
            Guid.NewGuid(), [firstKey, otherKey], "manager"));
        Assert.False(wrongWarehouse.IsSuccess);

        var creationRequest = new CreateReceivingOrderRequest(Guid.NewGuid(),
            [firstKey, firstKey, secondKey, unpostedKey], "manager");
        var first = Value(await create.ExecuteAsync(creationRequest));
        Assert.NotEqual(firstKey, first);
        Assert.NotEqual(secondKey, first);
        Assert.Equal(first, Value(await create.ExecuteAsync(new CreateReceivingOrderRequest(
            creationRequest.RequestId, [secondKey, firstKey, unpostedKey], "manager"))));
        Assert.Equal(first, Value(await create.ExecuteAsync(new CreateReceivingOrderRequest(
            Guid.NewGuid(), [secondKey, firstKey, unpostedKey], "manager"))));
        Assert.False((await create.ExecuteAsync(new CreateReceivingOrderRequest(
            creationRequest.RequestId, [otherKey], "manager"))).IsSuccess);

        await using var verify = database.CreateDbContext();
        Assert.Equal(1, await verify.ReceivingOrders.CountAsync(x => x.Id == first));
        Assert.Equal(2, await verify.CommandReceipts.CountAsync(x =>
            x.CommandType == "receiving.order.create" && x.ResultResourceId == first));
        var creationAudit = await verify.WarehouseAuditEvents.SingleAsync(x =>
            x.EventType == "receiving.order.created" && x.ReceivingOrderId == first);
        Assert.Equal("manager", creationAudit.ActorUserId);
        Assert.Equal(creationRequest.RequestId, creationAudit.RequestId);
        Assert.Equal("receiving.order.create", creationAudit.CommandType);
        Assert.Equal(3, JsonSerializer.Deserialize<Guid[]>(
            creationAudit.SourceVersionIdsJson)!.Length);
        var links = await verify.ReceivingOrderSourceLinks
            .Where(x => x.ReceivingOrderId == first && x.DetachedAtUtc == null)
            .ToListAsync();
        Assert.Equal(3, links.Count);
        var items = await verify.ReceivingOrderItems
            .Where(x => x.ReceivingOrderId == first).ToListAsync();
        Assert.Equal(2, items.Count);
        Assert.Equal(9m, items.Single(x => x.StockKeepingUnitId == skuId).CurrentPlanQuantity);
        Assert.Null(items.Single(x => x.StockKeepingUnitId == uncertainSkuId).CurrentPlanQuantity);
        Assert.All(items, x => Assert.Null(x.FactQuantity));
        Assert.Equal(1, await verify.ReceivingCycles.CountAsync(x => x.ReceivingOrderId == first));
        var unlinked = (await list.ListUnlinkedAsync())
            .Where(x => responses.ContainsKey(x.RefKey)).ToList();
        Assert.Single(unlinked);
        Assert.Equal(otherKey, unlinked[0].RefKey);

        var revision = (await verify.ReceivingOrders.SingleAsync(x => x.Id == first))
            .OperationalRevision;
        var firstSourceId = await verify.ReceivingSourceDocuments
            .Where(x => x.RefKey == firstKey).Select(x => x.Id).SingleAsync();
        responses[firstKey] = Source(firstKey, warehouseId, $$"""
            [{"LineNumber":"1","Номенклатура_Key":"{{skuId}}","Количество":6},
             {"LineNumber":"2","Номенклатура_Key":"{{uncertainSkuId}}","Количество":"unsupported"}]
            """);
        Assert.True((await synchronization.ExecuteAsync(firstKey)).IsSuccess);
        Assert.True((await synchronization.ExecuteAsync(firstKey)).IsSuccess);
        await using var updated = database.CreateDbContext();
        Assert.Equal(2, await updated.ReceivingSourceVersions.CountAsync(x =>
            x.ReceivingSourceDocumentId == firstSourceId));
        var current = await updated.ReceivingOrderItems
            .Where(x => x.ReceivingOrderId == first).ToListAsync();
        Assert.Equal(10m, current.Single(x => x.StockKeepingUnitId == skuId).CurrentPlanQuantity);
        Assert.Null(current.Single(x => x.StockKeepingUnitId == uncertainSkuId).CurrentPlanQuantity);
        Assert.All(current, x => Assert.Null(x.FactQuantity));
        Assert.Equal(revision + 1, (await updated.ReceivingOrders.SingleAsync(x =>
            x.Id == first)).OperationalRevision);
        Assert.False(await updated.InventoryMovements.AnyAsync(x => x.RecorderId == first));
    }

    private static string Source(Guid refKey, Guid warehouseId, string items, bool posted = true) => $$"""
        {"value":[{"Ref_Key":"{{refKey}}","Склад_Key":"{{warehouseId}}",
         "Posted":{{posted.ToString().ToLowerInvariant()}},"DeletionMark":false,"Number":"Test","Товары":{{items}}}]}
        """;

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class ConcurrentCreateStore(IReceivingOrderStore inner) : IReceivingOrderStore
    {
        private readonly TaskCompletionSource _ready = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivals;

        public Task<CommandReceiptSnapshot?> FindReceiptAsync(
            string userId, string commandType, Guid requestId, CancellationToken ct) =>
            inner.FindReceiptAsync(userId, commandType, requestId, ct);

        public Task<IReadOnlyList<ReceivingSourceState>> LoadSourcesAsync(
            IReadOnlyCollection<Guid> refKeys, CancellationToken ct) =>
            inner.LoadSourcesAsync(refKeys, ct);

        public Task<IReadOnlyList<ReceivingSourceState>> ListUnlinkedSourcesAsync(
            CancellationToken ct) => inner.ListUnlinkedSourcesAsync(ct);

        public Task<IReadOnlyList<ReceivingSourceState>> ListAllSourcesAsync(
            CancellationToken ct) => inner.ListAllSourcesAsync(ct);

        public async Task<OperationResult<Guid>> CreateAsync(
            ReceivingOrder order, IReadOnlyList<ReceivingOrderSourceLink> links,
            IReadOnlyList<ReceivingOrderItem> items,
            IReadOnlyList<ReceivingSourceExpectation> expectedSources,
            CommandReceiptSnapshot receipt, WarehouseAuditEvent createdAudit,
            CancellationToken ct)
        {
            if (Interlocked.Increment(ref _arrivals) == 2)
                _ready.TrySetResult();
            await _ready.Task.WaitAsync(TimeSpan.FromSeconds(20), ct);
            return await inner.CreateAsync(order, links, items, expectedSources,
                receipt, createdAudit, ct);
        }
    }

    private sealed class StubReader(IReadOnlyDictionary<Guid, string> responses) : IReceivingSourceReader
    {
        public Task<OperationResult<ReceivingSourceRead>> ReadAsync(Guid refKey, CancellationToken ct) =>
            Task.FromResult<OperationResult<ReceivingSourceRead>>(
                new ReceivingSourceRead(refKey, responses[refKey]));
    }

    private sealed class ManagerPermission : IWarehousePermissionStore
    {
        public Task<bool> HasManagerRoleAsync(string userId, CancellationToken ct) =>
            Task.FromResult(true);

        public Task<bool> HasReceivingRoleAsync(string userId, CancellationToken ct) =>
            Task.FromResult(true);
    }

    private sealed class ManagerAndOperatorPermission : IWarehousePermissionStore
    {
        public Task<bool> HasManagerRoleAsync(string userId, CancellationToken ct) =>
            Task.FromResult(userId == "manager");

        public Task<bool> HasReceivingRoleAsync(string userId, CancellationToken ct) =>
            Task.FromResult(userId is "manager" or "operator");
    }
}
