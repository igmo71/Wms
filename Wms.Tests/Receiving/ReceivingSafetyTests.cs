using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Wms.Application.Commands;
using Wms.Application.Inventory.Movements;
using Wms.Application.LicensePlateNumbers;
using Wms.Application.ReceivingOrders;
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

    [Fact]
    public async Task Concurrent_first_participants_leave_one_shared_receiving_location()
    {
        var warehouse = new Warehouse { Id = Guid.NewGuid(), Name = "Receiving participation" };
        var sku = new StockKeepingUnit { Id = Guid.NewGuid(), Name = "Participation SKU" };
        var zone = Zone(warehouse.Id, "RP", ZoneType.Receiving);
        var firstLocation = Location(warehouse.Id, zone, 1);
        var secondLocation = Location(warehouse.Id, zone, 2);
        var snapshot = new ReceivingOrderImportSnapshot(
            Guid.NewGuid(), false, true, "Participation", DateTime.UtcNow, warehouse.Id, null,
            ReceivingOrderStatus.ReadyForReceiving, default, WarehouseOperation.VendorReceipt,
            BusinessOperation.VendorPurchase, Guid.NewGuid(), default, Guid.NewGuid(), null,
            [new(1, sku.Id, 5m, 5m, null)]);

        await using (var seed = database.CreateDbContext())
        {
            seed.AddRange(warehouse, sku, zone, firstLocation, secondLocation, Value(ReceivingOrder.Create(snapshot, DateTimeOffset.UtcNow)));
            await seed.SaveChangesAsync();
        }

        var interceptor = new ParticipantSaveBarrier();
        var factory = database.WithInterceptor(interceptor);
        var service = ReceivingService(factory);
        var requests = new[] { Command("participant-a"), Command("participant-b") };
        var results = await Task.WhenAll(
            service.JoinReceivingAsync(new(snapshot.Id, firstLocation.Id), requests[0]),
            service.JoinReceivingAsync(new(snapshot.Id, secondLocation.Id), requests[1]));

        Assert.Single(results, result => result.IsSuccess);
        Error(results.Single(x => !x.IsSuccess), OperationErrorType.Conflict);

        var loser = results[0].IsSuccess ? 1 : 0;
        var retry = await ReceivingService(database).JoinReceivingAsync(
            new(snapshot.Id, loser == 0 ? firstLocation.Id : secondLocation.Id), requests[loser]);
        Success(retry);

        await using var verify = database.CreateDbContext();
        var order = await verify.ReceivingOrders.Include(x => x.Participants).SingleAsync(x => x.Id == snapshot.Id);
        Assert.Equal(ReceivingOrderStatus.InReceiving, order.Status);
        Assert.True(order.ReceivingLocationId is not null);
        Assert.Equal(2, order.Participants.Count);
        Assert.All(order.Participants, participant => Assert.Equal(order.Id, participant.ReceivingOrderId));
    }

    [Fact]
    public async Task Receiving_lpn_assignment_is_single_owner_and_cancelled_code_is_never_free_again()
    {
        var warehouse = new Warehouse { Id = Guid.NewGuid(), Name = "LPN assignment" };
        var sku = new StockKeepingUnit { Id = Guid.NewGuid(), Name = "LPN SKU" };
        var zone = Zone(warehouse.Id, "LA", ZoneType.Receiving);
        var location = Location(warehouse.Id, zone, 1);
        var firstOrder = StartedOrder(warehouse.Id, sku.Id, location.Id, "owner-a");
        var secondOrder = StartedOrder(warehouse.Id, sku.Id, location.Id, "owner-b");

        await using (var seed = database.CreateDbContext())
        {
            seed.AddRange(warehouse, sku, zone, location, firstOrder, secondOrder);
            await seed.SaveChangesAsync();
        }

        var lpnService = LpnService(database);
        var batchId = Value(await lpnService.IssueAsync(3, Command("issuer")));
        string[] codes;
        await using (var read = database.CreateDbContext())
        {
            codes = await read.LicensePlateNumbers
                .Where(x => x.BatchId == batchId)
                .OrderBy(x => x.Code)
                .Select(x => x.Code)
                .ToArrayAsync();
        }

        var sameLabelRaceService = LpnService(database.WithInterceptor(new ReceiptSaveBarrier()));
        var sameLabelRace = await Task.WhenAll(
            sameLabelRaceService.OpenForReceivingAsync(new(firstOrder.Id, codes[0]), Command("owner-a")),
            sameLabelRaceService.OpenForReceivingAsync(new(secondOrder.Id, codes[0]), Command("owner-b")));
        Assert.Single(sameLabelRace, x => x.IsSuccess);
        Error(sameLabelRace.Single(x => !x.IsSuccess), OperationErrorType.Conflict);

        var targetOrder = sameLabelRace[0].IsSuccess ? firstOrder : secondOrder;
        var targetOwner = sameLabelRace[0].IsSuccess ? "owner-a" : "owner-b";
        var assignedId = Value(sameLabelRace.Single(x => x.IsSuccess));
        Success(await lpnService.CancelEmptyReceivingAsync(
            new(targetOrder.Id, assignedId), Command(targetOwner)));

        var oneOpenRaceService = LpnService(database.WithInterceptor(new ReceiptSaveBarrier()));
        var oneOpenRace = await Task.WhenAll(
            oneOpenRaceService.OpenForReceivingAsync(new(targetOrder.Id, codes[1]), Command(targetOwner)),
            oneOpenRaceService.OpenForReceivingAsync(new(targetOrder.Id, codes[2]), Command(targetOwner)));
        Assert.Single(oneOpenRace, x => x.IsSuccess);
        Error(oneOpenRace.Single(x => !x.IsSuccess), OperationErrorType.Conflict);

        await using var verify = database.CreateDbContext();
        var assigned = await verify.LicensePlateNumbers.SingleAsync(x => x.Code == codes[0]);
        Assert.Equal(targetOrder.Id, assigned.ReceivingOrderId);
        Assert.Equal(LicensePlateNumberStatus.Cancelled, assigned.Status);

        var openId = Value(oneOpenRace.Single(x => x.IsSuccess));
        Success(await lpnService.CancelEmptyReceivingAsync(
            new(targetOrder.Id, openId), Command(targetOwner)));
        var nextCode = oneOpenRace[0].IsSuccess ? codes[2] : codes[1];
        var next = await lpnService.OpenForReceivingAsync(
            new(targetOrder.Id, nextCode), Command(targetOwner));
        Success(next);
        var reopenCancelled = await lpnService.OpenForReceivingAsync(
            new(targetOrder.Id, codes[0]), Command(targetOwner));
        Assert.Equal(assigned.Id, Value(reopenCancelled));

        Assert.Equal(LicensePlateNumberStatus.Cancelled, assigned.Status);
        Assert.Equal(targetOrder.Id, assigned.ReceivingOrderId);
    }

    [Fact]
    public async Task Concurrent_lpn_counts_preserve_explicit_zero_aggregate_and_manager_reopen()
    {
        var warehouse = new Warehouse { Id = Guid.NewGuid(), Name = "LPN count" };
        var sku = new StockKeepingUnit { Id = Guid.NewGuid(), Name = "Counted SKU" };
        var zone = Zone(warehouse.Id, "LC", ZoneType.Receiving);
        var location = Location(warehouse.Id, zone, 1);
        var order = StartedOrder(warehouse.Id, sku.Id, location.Id, "counter-a");
        Success(order.JoinReceiving(DateTimeOffset.UtcNow, "counter-b"));

        await using (var seed = database.CreateDbContext())
        {
            seed.AddRange(warehouse, sku, zone, location, order);
            await seed.SaveChangesAsync();
        }

        var service = LpnService(database);
        var batchId = Value(await service.IssueAsync(2, Command("issuer")));
        string[] codes;
        await using (var read = database.CreateDbContext())
        {
            codes = await read.LicensePlateNumbers
                .Where(x => x.BatchId == batchId)
                .OrderBy(x => x.Code)
                .Select(x => x.Code)
                .ToArrayAsync();
        }
        var firstLpn = Value(await service.OpenForReceivingAsync(
            new(order.Id, codes[0]), Command("counter-a")));
        var secondLpn = Value(await service.OpenForReceivingAsync(
            new(order.Id, codes[1]), Command("counter-b")));

        var racingService = LpnService(database.WithInterceptor(new ReceiptSaveBarrier()));
        var commands = new[]
        {
            new SetReceivingLicensePlateNumberItemQuantityCommand(order.Id, firstLpn, sku.Id, 2m),
            new SetReceivingLicensePlateNumberItemQuantityCommand(order.Id, secondLpn, sku.Id, 0m)
        };
        var contexts = new[] { Command("counter-a"), Command("counter-b") };
        var race = await Task.WhenAll(
            racingService.SetReceivingItemQuantityAsync(commands[0], contexts[0], false),
            racingService.SetReceivingItemQuantityAsync(commands[1], contexts[1], false));
        Assert.Contains(race, x => !x.IsSuccess);
        for (var index = 0; index < race.Length; index++)
        {
            if (race[index].IsSuccess)
                continue;
            Error(race[index], OperationErrorType.Conflict);
            Success(await service.SetReceivingItemQuantityAsync(
                commands[index], Command(contexts[index].UserId), false));
        }
        Success(await service.CloseReceivingAsync(
            new(order.Id, firstLpn), Command("counter-a"), false));
        Success(await service.CloseReceivingAsync(
            new(order.Id, secondLpn), Command("counter-b"), false));
        Success(await service.ReopenReceivingAsync(
            new(order.Id, firstLpn), Command("manager"), true));
        Success(await service.SetReceivingItemQuantityAsync(
            new(order.Id, firstLpn, sku.Id, 3m), Command("manager"), true));

        await using var verify = database.CreateDbContext();
        var savedOrder = await verify.ReceivingOrders
            .Include(x => x.Items)
            .Include(x => x.LicensePlateNumbers)
                .ThenInclude(x => x.Items)
            .SingleAsync(x => x.Id == order.Id);
        Assert.Equal(3m, savedOrder.Items.Single().FactQuantity);
        Assert.Equal(2, savedOrder.LicensePlateNumbers.SelectMany(x => x.Items).Count());
        Assert.Contains(savedOrder.LicensePlateNumbers.SelectMany(x => x.Items), x => x.Quantity == 0m);
        Assert.Equal(LicensePlateNumberStatus.Open,
            savedOrder.LicensePlateNumbers.Single(x => x.Id == firstLpn).Status);
        Assert.Equal("manager",
            savedOrder.LicensePlateNumbers.Single(x => x.Id == firstLpn).ReopenedBy);
    }

    [Fact]
    public async Task Completion_racing_lpn_change_rolls_back_and_repeat_does_not_receive_twice()
    {
        var warehouse = new Warehouse { Id = Guid.NewGuid(), Name = "LPN completion" };
        var sku = new StockKeepingUnit { Id = Guid.NewGuid(), Name = "Completion SKU" };
        var zone = Zone(warehouse.Id, "LF", ZoneType.Receiving);
        var location = Location(warehouse.Id, zone, 1);
        var snapshot = new ReceivingOrderImportSnapshot(
            Guid.NewGuid(), false, true, "Completion", DateTime.UtcNow, warehouse.Id, null,
            ReceivingOrderStatus.ReadyForReceiving, default, WarehouseOperation.VendorReceipt,
            BusinessOperation.VendorPurchase, Guid.NewGuid(), default, Guid.NewGuid(), null,
            [new(1, sku.Id, 5m, 5m, null)]);
        var order = Value(ReceivingOrder.Create(snapshot, DateTimeOffset.UtcNow));
        Success(order.SetReceivingLocation(location.Id));
        Success(order.SetInReceiving(DateTimeOffset.UtcNow, "manager"));
        Success(order.JoinReceiving(DateTimeOffset.UtcNow, "manager"));

        await using (var seed = database.CreateDbContext())
        {
            seed.AddRange(warehouse, sku, zone, location, order);
            await seed.SaveChangesAsync();
        }

        var lpnService = LpnService(database);
        Guid batchId = Value(await lpnService.IssueAsync(1, Command("issuer")));
        string code;
        await using (var read = database.CreateDbContext())
            code = await read.LicensePlateNumbers.Where(x => x.BatchId == batchId).Select(x => x.Code).SingleAsync();
        Guid lpnId = Value(await lpnService.OpenForReceivingAsync(new(order.Id, code), Command("manager")));
        Success(await lpnService.SetReceivingItemQuantityAsync(
            new(order.Id, lpnId, sku.Id, 4m), Command("manager"), true));
        Success(await lpnService.CloseReceivingAsync(new(order.Id, lpnId), Command("manager"), true));

        var source = new FixedSource(snapshot);
        var synchronization = new ReceivingOrderSynchronizationService(
            database, source, NullLogger<ReceivingOrderSynchronizationService>.Instance);
        var preview = Value(await synchronization.CheckCompletionAsync(order.Id));
        Assert.True(preview.RequiresManager);

        var barrier = new CompletionReceiptBarrier();
        var racingService = ReceivingService(database.WithInterceptor(barrier), source);
        var racingContext = Command("manager");
        var racingCommand = new CompleteReceivingCommand(
            order.Id, null, preview.Assessment.Fingerprint, "Недостача при приёмке");
        Task<OperationResult<Guid>> racingCompletion = racingService.CompleteReceivingAsync(
            racingCommand, racingContext, true);
        await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));

        Success(await lpnService.ReopenReceivingAsync(new(order.Id, lpnId), Command("manager"), true));
        barrier.Release.TrySetResult();
        Error(await racingCompletion, OperationErrorType.Conflict);

        await using (var failed = database.CreateDbContext())
        {
            Assert.Equal(ReceivingOrderStatus.InReceiving,
                (await failed.ReceivingOrders.SingleAsync(x => x.Id == order.Id)).Status);
            Assert.False(await failed.InventoryMovements.AnyAsync(x => x.RecorderId == order.Id));
            Assert.False(await failed.InventoryBalances.AnyAsync(x => x.StorageLocationId == location.Id && x.StockKeepingUnitId == sku.Id));
            Assert.False(await failed.CommandReceipts.AnyAsync(x => x.RequestId == racingContext.RequestId));
        }

        Success(await lpnService.CloseReceivingAsync(new(order.Id, lpnId), Command("manager"), true));
        var fresh = Value(await synchronization.CheckCompletionAsync(order.Id));
        var successContext = Command("manager");
        var successCommand = new CompleteReceivingCommand(
            order.Id, null, fresh.Assessment.Fingerprint, "Недостача при приёмке");
        var service = ReceivingService(database, source);
        Value(await service.CompleteReceivingAsync(successCommand, successContext, true));
        int callsAfterSuccess = source.CallCount;
        Value(await service.CompleteReceivingAsync(successCommand, successContext, true));
        Assert.Equal(callsAfterSuccess, source.CallCount);

        await using var verify = database.CreateDbContext();
        var completed = await verify.ReceivingOrders.SingleAsync(x => x.Id == order.Id);
        Assert.Equal(ReceivingOrderStatus.Received, completed.Status);
        Assert.Equal("manager", completed.CompletionApprovedBy);
        Assert.Equal("Недостача при приёмке", completed.CompletionApprovalReason);
        Assert.False(string.IsNullOrWhiteSpace(completed.CompletionOriginalPlanJson));
        Assert.False(string.IsNullOrWhiteSpace(completed.CompletionCurrentPlanJson));
        Assert.Equal(1, await verify.InventoryMovements.CountAsync(x => x.RecorderId == order.Id));
        Assert.Equal(1, await verify.InventoryTurnovers.CountAsync(x => x.InventoryMovement!.RecorderId == order.Id));
        Assert.Equal(4m, (await verify.InventoryBalances.SingleAsync(
            x => x.StorageLocationId == location.Id && x.StockKeepingUnitId == sku.Id)).Quantity);
        Assert.Equal(1, await verify.CommandReceipts.CountAsync(x => x.RequestId == successContext.RequestId));
    }

    [Fact]
    public async Task Lpn_putaway_requires_completion_and_concurrent_overspend_posts_only_once()
    {
        var warehouse = new Warehouse { Id = Guid.NewGuid(), Name = "LPN putaway" };
        var sku = new StockKeepingUnit { Id = Guid.NewGuid(), Name = "Putaway SKU", Code = "PUT-1" };
        var receivingZone = Zone(warehouse.Id, "LR", ZoneType.Receiving);
        var storageZone = Zone(warehouse.Id, "LS", ZoneType.Storage);
        var sourceLocation = Location(warehouse.Id, receivingZone, 1);
        var firstDestination = Location(warehouse.Id, storageZone, 1);
        var secondDestination = Location(warehouse.Id, storageZone, 2);
        var snapshot = new ReceivingOrderImportSnapshot(
            Guid.NewGuid(), false, true, "LPN putaway", DateTime.UtcNow, warehouse.Id, null,
            ReceivingOrderStatus.ReadyForReceiving, default, WarehouseOperation.VendorReceipt,
            BusinessOperation.VendorPurchase, Guid.NewGuid(), default, Guid.NewGuid(), null,
            [new(1, sku.Id, 10m, 10m, null)]);
        var order = Value(ReceivingOrder.Create(snapshot, DateTimeOffset.UtcNow));
        Success(order.SetReceivingLocation(sourceLocation.Id));
        Success(order.SetInReceiving(DateTimeOffset.UtcNow, "worker-a"));
        Success(order.JoinReceiving(DateTimeOffset.UtcNow, "worker-a"));
        Success(order.JoinReceiving(DateTimeOffset.UtcNow, "worker-b"));

        await using (var seed = database.CreateDbContext())
        {
            seed.AddRange(
                warehouse, sku, receivingZone, storageZone,
                sourceLocation, firstDestination, secondDestination, order);
            await seed.SaveChangesAsync();
        }

        var lpnService = LpnService(database);
        var batchId = Value(await lpnService.IssueAsync(2, Command("issuer")));
        string[] codes;
        await using (var read = database.CreateDbContext())
            codes = await read.LicensePlateNumbers
                .Where(x => x.BatchId == batchId)
                .OrderBy(x => x.Code)
                .Select(x => x.Code)
                .ToArrayAsync();

        var firstLpnId = Value(await lpnService.OpenForReceivingAsync(
            new(order.Id, codes[0]), Command("worker-a")));
        Success(await lpnService.SetReceivingItemQuantityAsync(
            new(order.Id, firstLpnId, sku.Id, 5m), Command("worker-a"), false));
        Success(await lpnService.CloseReceivingAsync(
            new(order.Id, firstLpnId), Command("worker-a"), false));
        var secondLpnId = Value(await lpnService.OpenForReceivingAsync(
            new(order.Id, codes[1]), Command("worker-b")));
        Success(await lpnService.SetReceivingItemQuantityAsync(
            new(order.Id, secondLpnId, sku.Id, 5m), Command("worker-b"), false));
        Success(await lpnService.CloseReceivingAsync(
            new(order.Id, secondLpnId), Command("worker-b"), false));

        Guid firstItemId;
        await using (var read = database.CreateDbContext())
            firstItemId = await read.LicensePlateNumberItems
                .Where(x => x.LicensePlateNumberId == firstLpnId && x.StockKeepingUnitId == sku.Id)
                .Select(x => x.Id)
                .SingleAsync();

        var beforeCompletion = Command("worker-a");
        Error(await PutawayService(database).PutawayAsync(
            new(firstLpnId, firstItemId, firstDestination.Id, 1m), beforeCompletion),
            OperationErrorType.Invalid);

        var source = new FixedSource(snapshot);
        var synchronization = new ReceivingOrderSynchronizationService(
            database, source, NullLogger<ReceivingOrderSynchronizationService>.Instance);
        var preview = Value(await synchronization.CheckCompletionAsync(order.Id));
        Value(await ReceivingService(database, source).CompleteReceivingAsync(
            new(order.Id, null, preview.Assessment.Fingerprint, null),
            Command("worker-a"),
            false));

        var barrier = new ReceiptSaveBarrier();
        var racingService = PutawayService(database.WithInterceptor(barrier));
        var firstContext = Command("worker-a");
        var secondContext = Command("worker-b");
        var firstCommand = new PutawayLicensePlateNumberCommand(
            firstLpnId, firstItemId, firstDestination.Id, 3m);
        var secondCommand = new PutawayLicensePlateNumberCommand(
            firstLpnId, firstItemId, secondDestination.Id, 3m);
        var results = await Task.WhenAll(
            racingService.PutawayAsync(firstCommand, firstContext),
            racingService.PutawayAsync(secondCommand, secondContext));

        var resultSummary = string.Join(" | ", results.Select(x =>
            x.IsSuccess ? "success" : $"{x.Error?.Type}: {x.Error?.Message}"));
        Assert.True(results.Count(x => x.IsSuccess) == 1, resultSummary);
        Assert.True(
            results.Count(x => x.Error?.Type == OperationErrorType.Conflict) == 1,
            resultSummary);

        await using (var verify = database.CreateDbContext())
        {
            Assert.Equal(1, await verify.LicensePlateNumberPutaways.CountAsync(
                x => x.LicensePlateNumberId == firstLpnId));
            Assert.Equal(1, await verify.InventoryMovements.CountAsync(
                x => x.PostedAtUtc != null && x.RecorderId == order.Id
                    && x.SourceStorageLocationId == sourceLocation.Id));
            Assert.Equal(7m, (await verify.InventoryBalances.SingleAsync(
                x => x.StorageLocationId == sourceLocation.Id
                    && x.StockKeepingUnitId == sku.Id)).Quantity);
            Assert.Equal(1, await verify.CommandReceipts.CountAsync(
                x => x.RequestId == firstContext.RequestId || x.RequestId == secondContext.RequestId));
        }

        var winningCommand = results[0].IsSuccess ? firstCommand : secondCommand;
        var winningContext = results[0].IsSuccess ? firstContext : secondContext;
        Value(await PutawayService(database).PutawayAsync(winningCommand, winningContext));

        await using var replayVerify = database.CreateDbContext();
        Assert.Equal(1, await replayVerify.LicensePlateNumberPutaways.CountAsync(
            x => x.LicensePlateNumberId == firstLpnId));
        Assert.Equal(7m, (await replayVerify.InventoryBalances.SingleAsync(
            x => x.StorageLocationId == sourceLocation.Id
                && x.StockKeepingUnitId == sku.Id)).Quantity);
    }

    private static ReceivingOrder StartedOrder(
        Guid warehouseId,
        Guid skuId,
        Guid locationId,
        string userId)
    {
        var snapshot = new ReceivingOrderImportSnapshot(
            Guid.NewGuid(), false, true, Guid.NewGuid().ToString("N"), DateTime.UtcNow,
            warehouseId, null, ReceivingOrderStatus.ReadyForReceiving, default,
            WarehouseOperation.VendorReceipt, BusinessOperation.VendorPurchase,
            Guid.NewGuid(), default, Guid.NewGuid(), null,
            [new(1, skuId, 1m, 1m, null)]);
        var order = Value(ReceivingOrder.Create(snapshot, DateTimeOffset.UtcNow));
        Success(order.SetReceivingLocation(locationId));
        Success(order.SetInReceiving(DateTimeOffset.UtcNow, userId));
        Success(order.JoinReceiving(DateTimeOffset.UtcNow, userId));
        return order;
    }

    private static LicensePlateNumberService LpnService(IDbContextFactory<ApplicationDbContext> factory) =>
        new(new CommandExecutor(factory), factory);

    private static ReceivingOrderCommandService ReceivingService(
        IDbContextFactory<ApplicationDbContext> factory,
        IReceivingOrderSource? source = null) =>
        new(
            new CommandExecutor(factory),
            new InventoryPostingService(NullLogger<InventoryPostingService>.Instance),
            new ReceivingOrderSynchronizationService(factory, source ?? new RejectingSource(), NullLogger<ReceivingOrderSynchronizationService>.Instance),
            NullLogger<ReceivingOrderCommandService>.Instance);

    private static LicensePlateNumberPutawayService PutawayService(
        IDbContextFactory<ApplicationDbContext> factory) =>
        new(
            new CommandExecutor(factory),
            factory,
            new InventoryPostingService(NullLogger<InventoryPostingService>.Instance));

    private sealed class ParticipantSaveBarrier : SaveChangesInterceptor
    {
        private int _arrivals;
        private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<ReceivingOrderParticipant>().Any(x => x.State == EntityState.Added))
            {
                if (Interlocked.Increment(ref _arrivals) == 2)
                    _ready.TrySetResult();
                await _ready.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            }
            return result;
        }
    }

    private sealed class RejectingSource : IReceivingOrderSource
    {
        public Task<OperationResult<ReceivingOrderImportSnapshot>> GetSnapshotAsync(Guid orderId, CancellationToken ct = default) =>
            throw new InvalidOperationException("Joining receiving must not call 1C.");
    }

    private sealed class FixedSource(ReceivingOrderImportSnapshot snapshot) : IReceivingOrderSource
    {
        private int _callCount;
        public int CallCount => Volatile.Read(ref _callCount);

        public Task<OperationResult<ReceivingOrderImportSnapshot>> GetSnapshotAsync(
            Guid orderId,
            CancellationToken ct = default)
        {
            Interlocked.Increment(ref _callCount);
            return Task.FromResult<OperationResult<ReceivingOrderImportSnapshot>>(
                orderId == snapshot.Id
                    ? snapshot
                    : OperationError.NotFound("Snapshot not found."));
        }
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
