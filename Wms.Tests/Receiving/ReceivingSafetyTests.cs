using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Wms.Application.Commands;
using Wms.Application.Inventory.Movements;
using Wms.Application.Inventory.Transfers;
using Wms.Application.LicensePlateNumbers;
using Wms.Application.ReceivingOrders;
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
    public async Task Receiving_lpn_assignment_allows_multiple_open_lpn_and_cancelled_code_is_never_free_again()
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

        var multipleOpenService = LpnService(database.WithInterceptor(new ReceiptSaveBarrier()));
        var multipleOpen = await Task.WhenAll(
            multipleOpenService.OpenForReceivingAsync(new(targetOrder.Id, codes[1]), Command(targetOwner)),
            multipleOpenService.OpenForReceivingAsync(new(targetOrder.Id, codes[2]), Command(targetOwner)));
        Assert.Contains(multipleOpen, result => !result.IsSuccess);
        var openIds = new List<Guid>();
        for (var index = 0; index < multipleOpen.Length; index++)
        {
            if (multipleOpen[index].IsSuccess)
            {
                openIds.Add(Value(multipleOpen[index]));
                continue;
            }

            Error(multipleOpen[index], OperationErrorType.Conflict);
            openIds.Add(Value(await lpnService.OpenForReceivingAsync(
                new(targetOrder.Id, codes[index + 1]), Command(targetOwner))));
        }
        Assert.Equal(2, openIds.Distinct().Count());

        await using var verify = database.CreateDbContext();
        var assigned = await verify.LicensePlateNumbers.SingleAsync(x => x.Code == codes[0]);
        Assert.Equal(targetOrder.Id, assigned.ReceivingOrderId);
        Assert.Equal(LicensePlateNumberStatus.Cancelled, assigned.Status);

        var firstOpenId = openIds[0];
        Success(await lpnService.CloseReceivingAsync(
            new(targetOrder.Id, firstOpenId), Command(targetOwner), false));
        Success(await lpnService.ReopenReceivingAsync(
            new(targetOrder.Id, firstOpenId), Command("manager"), true));
        var reopenCancelled = await lpnService.OpenForReceivingAsync(
            new(targetOrder.Id, codes[0]), Command(targetOwner));
        Assert.Equal(assigned.Id, Value(reopenCancelled));

        var savedLpns = await verify.LicensePlateNumbers
            .AsNoTracking()
            .Where(x => x.ReceivingOrderId == targetOrder.Id)
            .ToListAsync();
        Assert.Equal(2, savedLpns.Count(x => x.Status == LicensePlateNumberStatus.Open));
        Assert.Contains(savedLpns, x => x.Id == assigned.Id && x.Status == LicensePlateNumberStatus.Cancelled);
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
        Success(order.SetInReceiving(DateTimeOffset.UtcNow, "receiver"));
        Success(order.JoinReceiving(DateTimeOffset.UtcNow, "receiver"));

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
        Guid lpnId = Value(await lpnService.OpenForReceivingAsync(new(order.Id, code), Command("receiver")));
        Success(await lpnService.SetReceivingItemQuantityAsync(
            new(order.Id, lpnId, sku.Id, 4m), Command("receiver"), false));
        Success(await lpnService.CloseReceivingAsync(new(order.Id, lpnId), Command("receiver"), false));

        var source = new FixedSource(snapshot);
        var synchronization = new ReceivingOrderSynchronizationService(
            database, source, NullLogger<ReceivingOrderSynchronizationService>.Instance);
        var preview = Value(await synchronization.CheckCompletionAsync(order.Id));
        Assert.True(preview.RequiresManager);

        // Participation alone cannot authorize a discrepancy; Manager authority
        // still requires a reason even when the caller is not a participant.
        var guardedService = ReceivingService(database, source);
        var approvalCommand = new CompleteReceivingCommand(
            order.Id, null, preview.Assessment.Fingerprint, "Недостача при приёмке");
        Error(await guardedService.CompleteReceivingAsync(
            approvalCommand, Command("receiver"), false), OperationErrorType.Conflict);
        Error(await guardedService.CompleteReceivingAsync(
            approvalCommand, Command("administrator"), false), OperationErrorType.Conflict);
        Error(await guardedService.CompleteReceivingAsync(
            approvalCommand with { ManagerReason = null }, Command("manager"), true),
            OperationErrorType.Conflict);

        // A Manager outside the participants cannot use ordinary exact completion.
        var exactSnapshot = snapshot with { Items = [new(1, sku.Id, 4m, 4m, null)] };
        var exactSource = new FixedSource(exactSnapshot);
        var exactPreview = Value(await new ReceivingOrderSynchronizationService(
            database, exactSource, NullLogger<ReceivingOrderSynchronizationService>.Instance)
            .CheckCompletionAsync(order.Id));
        Assert.False(exactPreview.RequiresManager);
        Error(await ReceivingService(database, exactSource).CompleteReceivingAsync(
            new(order.Id, null, exactPreview.Assessment.Fingerprint, null),
            Command("manager"), true), OperationErrorType.Conflict);

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
        Assert.Equal("manager", completed.CompletedBy);
        var participants = await verify.ReceivingOrderParticipants
            .Where(x => x.ReceivingOrderId == order.Id).ToListAsync();
        Assert.Equal("receiver", Assert.Single(participants).UserId);
        var acceptedLpn = await verify.LicensePlateNumbers.Include(x => x.Items)
            .SingleAsync(x => x.Id == lpnId);
        Assert.Equal("receiver", acceptedLpn.ResponsibleUserId);
        Assert.Equal("receiver", Assert.Single(acceptedLpn.Items).CreatedBy);
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

    [Fact]
    public async Task Lpn_putaway_recommendations_classify_order_and_revalidate_a_changed_location()
    {
        var warehouse = new Warehouse { Id = Guid.NewGuid(), Name = "LPN recommendations" };
        var sku = new StockKeepingUnit { Id = Guid.NewGuid(), Name = "Recommended SKU", Code = "REC-1" };
        var otherSku = new StockKeepingUnit { Id = Guid.NewGuid(), Name = "Other SKU", Code = "REC-2" };
        var receivingZone = Zone(warehouse.Id, "RR", ZoneType.Receiving);
        var storageZone = Zone(warehouse.Id, "RS", ZoneType.Storage);
        var sourceLocation = Location(warehouse.Id, receivingZone, 1);
        var currentAddressFirst = RecommendationLocation(warehouse.Id, storageZone, 10, "10", 10);
        var currentAddressSecond = RecommendationLocation(warehouse.Id, storageZone, 11, "11", 10);
        var currentWithoutSequence = RecommendationLocation(warehouse.Id, storageZone, 12, "12", null);
        var historical = RecommendationLocation(warehouse.Id, storageZone, 21, "21", 5);
        var emptyWithOtherHistory = RecommendationLocation(warehouse.Id, storageZone, 31, "31", null);
        var pristine = RecommendationLocation(warehouse.Id, storageZone, 32, "32", null);
        var occupiedByOtherSku = RecommendationLocation(warehouse.Id, storageZone, 40, "40", 1);
        var snapshot = new ReceivingOrderImportSnapshot(
            Guid.NewGuid(), false, true, "LPN recommendations", DateTime.UtcNow, warehouse.Id, null,
            ReceivingOrderStatus.ReadyForReceiving, default, WarehouseOperation.VendorReceipt,
            BusinessOperation.VendorPurchase, Guid.NewGuid(), default, Guid.NewGuid(), null,
            [new(1, sku.Id, 2m, 2m, null)]);
        var order = Value(ReceivingOrder.Create(snapshot, DateTimeOffset.UtcNow));
        Success(order.SetReceivingLocation(sourceLocation.Id));
        Success(order.SetInReceiving(DateTimeOffset.UtcNow, "worker"));
        Success(order.JoinReceiving(DateTimeOffset.UtcNow, "worker"));

        await using (var seed = database.CreateDbContext())
        {
            seed.AddRange(
                warehouse, sku, otherSku, receivingZone, storageZone, sourceLocation,
                currentAddressFirst, currentAddressSecond, currentWithoutSequence,
                historical, emptyWithOtherHistory, pristine,
                occupiedByOtherSku, order,
                Balance(warehouse.Id, currentAddressFirst.Id, sku.Id, 3m),
                Balance(warehouse.Id, currentAddressSecond.Id, sku.Id, 2m),
                Balance(warehouse.Id, currentWithoutSequence.Id, sku.Id, 1m),
                Balance(warehouse.Id, historical.Id, sku.Id, 0m),
                Balance(warehouse.Id, emptyWithOtherHistory.Id, otherSku.Id, 0m),
                Balance(warehouse.Id, occupiedByOtherSku.Id, sku.Id, 0m),
                Balance(warehouse.Id, occupiedByOtherSku.Id, otherSku.Id, 1m));
            await seed.SaveChangesAsync();
        }

        var lpnService = LpnService(database);
        var batchId = Value(await lpnService.IssueAsync(1, Command("issuer")));
        string code;
        await using (var read = database.CreateDbContext())
            code = await read.LicensePlateNumbers
                .Where(x => x.BatchId == batchId)
                .Select(x => x.Code)
                .SingleAsync();
        var lpnId = Value(await lpnService.OpenForReceivingAsync(
            new(order.Id, code), Command("worker")));
        Success(await lpnService.SetReceivingItemQuantityAsync(
            new(order.Id, lpnId, sku.Id, 2m), Command("worker"), false));
        Success(await lpnService.CloseReceivingAsync(
            new(order.Id, lpnId), Command("worker"), false));

        var source = new FixedSource(snapshot);
        var synchronization = new ReceivingOrderSynchronizationService(
            database, source, NullLogger<ReceivingOrderSynchronizationService>.Instance);
        var preview = Value(await synchronization.CheckCompletionAsync(order.Id));
        Value(await ReceivingService(database, source).CompleteReceivingAsync(
            new(order.Id, null, preview.Assessment.Fingerprint, null),
            Command("worker"),
            false));

        Guid itemId;
        await using (var read = database.CreateDbContext())
            itemId = await read.LicensePlateNumberItems
                .Where(x => x.LicensePlateNumberId == lpnId && x.StockKeepingUnitId == sku.Id)
                .Select(x => x.Id)
                .SingleAsync();

        var recommendations = Value(await PutawayService(database).GetSelectionAsync(lpnId, itemId))
            .Recommendations;
        Assert.Equal(
            new[]
            {
                currentAddressFirst.Id,
                currentAddressSecond.Id,
                currentWithoutSequence.Id,
                historical.Id
            },
            recommendations.Select(x => x.StorageLocationId));
        Assert.Equal(
            new[]
            {
                LicensePlateNumberPutawayRecommendationCategory.CurrentSkuStock,
                LicensePlateNumberPutawayRecommendationCategory.CurrentSkuStock,
                LicensePlateNumberPutawayRecommendationCategory.CurrentSkuStock,
                LicensePlateNumberPutawayRecommendationCategory.EmptyWithSkuBalanceHistory
            },
            recommendations.Select(x => x.Category));
        Assert.Equal(
            new decimal?[] { 3m, 2m, 1m, null },
            recommendations.Select(x => x.CurrentSkuQuantity));
        Assert.Equal(recommendations.Count, recommendations.Select(x => x.StorageLocationId).Distinct().Count());
        Assert.DoesNotContain(recommendations, x => x.StorageLocationId == occupiedByOtherSku.Id);

        await using (var lockContext = database.CreateDbContext())
        {
            lockContext.StorageLocationLocks.Add(Value(StorageLocationLock.CreateManual(
                currentAddressFirst.Id, "Changed after recommendations", DateTimeOffset.UtcNow, "manager")));
            await lockContext.SaveChangesAsync();
        }

        Error(await PutawayService(database).PutawayAsync(
            new(lpnId, itemId, currentAddressFirst.Id, 1m), Command("worker")),
            OperationErrorType.Conflict);
    }

    [Fact]
    public async Task Two_workers_complete_and_putaway_lpn_receiving_without_losing_history()
    {
        var warehouse = new Warehouse { Id = Guid.NewGuid(), Name = "End-to-end LPN" };
        var bolts = new StockKeepingUnit { Id = Guid.NewGuid(), Name = "Bolts", Code = "BOLT" };
        var nuts = new StockKeepingUnit { Id = Guid.NewGuid(), Name = "Nuts", Code = "NUT" };
        var receivingZone = Zone(warehouse.Id, "ER", ZoneType.Receiving);
        var storageZone = Zone(warehouse.Id, "ES", ZoneType.Storage);
        var receivingLocation = Location(warehouse.Id, receivingZone, 1);
        var firstDestination = RecommendationLocation(warehouse.Id, storageZone, 1, "01", 1);
        var secondDestination = RecommendationLocation(warehouse.Id, storageZone, 2, "02", 2);
        var snapshot = new ReceivingOrderImportSnapshot(
            Guid.NewGuid(), false, true, "End-to-end LPN", DateTime.UtcNow, warehouse.Id, null,
            ReceivingOrderStatus.ReadyForReceiving, default, WarehouseOperation.VendorReceipt,
            BusinessOperation.VendorPurchase, Guid.NewGuid(), default, Guid.NewGuid(), null,
            [new(1, bolts.Id, 100m, 100m, null), new(2, nuts.Id, 20m, 20m, null)]);
        var order = Value(ReceivingOrder.Create(snapshot, DateTimeOffset.UtcNow));
        Success(order.SetReceivingLocation(receivingLocation.Id));
        Success(order.SetInReceiving(DateTimeOffset.UtcNow, "worker-a"));
        Success(order.JoinReceiving(DateTimeOffset.UtcNow, "worker-a"));
        Success(order.JoinReceiving(DateTimeOffset.UtcNow, "worker-b"));

        await using (var seed = database.CreateDbContext())
        {
            seed.AddRange(
                warehouse, bolts, nuts, receivingZone, storageZone,
                receivingLocation, firstDestination, secondDestination, order);
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
            new(order.Id, firstLpnId, bolts.Id, 40m), Command("worker-a"), false));
        Success(await lpnService.CloseReceivingAsync(
            new(order.Id, firstLpnId), Command("worker-a"), false));

        var secondLpnId = Value(await lpnService.OpenForReceivingAsync(
            new(order.Id, codes[1]), Command("worker-b")));
        Success(await lpnService.SetReceivingItemQuantityAsync(
            new(order.Id, secondLpnId, bolts.Id, 60m), Command("worker-b"), false));
        Success(await lpnService.SetReceivingItemQuantityAsync(
            new(order.Id, secondLpnId, nuts.Id, 20m), Command("worker-b"), false));
        Success(await lpnService.CloseReceivingAsync(
            new(order.Id, secondLpnId), Command("worker-b"), false));

        await using (var beforeCompletion = database.CreateDbContext())
            Assert.Empty(await beforeCompletion.InventoryBalances
                .Where(x => x.WarehouseId == warehouse.Id)
                .ToListAsync());

        var source = new FixedSource(snapshot);
        var synchronization = new ReceivingOrderSynchronizationService(
            database, source, NullLogger<ReceivingOrderSynchronizationService>.Instance);
        var preview = Value(await synchronization.CheckCompletionAsync(order.Id));
        var completionContext = Command("worker-a");
        var receivingService = ReceivingService(database, source);
        Value(await receivingService.CompleteReceivingAsync(
            new(order.Id, null, preview.Assessment.Fingerprint, null),
            completionContext,
            false));
        Value(await receivingService.CompleteReceivingAsync(
            new(order.Id, null, preview.Assessment.Fingerprint, null),
            completionContext,
            false));

        Dictionary<(Guid LpnId, Guid SkuId), Guid> itemIds;
        await using (var read = database.CreateDbContext())
            itemIds = await read.LicensePlateNumberItems
                .Where(x => x.LicensePlateNumberId == firstLpnId || x.LicensePlateNumberId == secondLpnId)
                .ToDictionaryAsync(x => (x.LicensePlateNumberId, x.StockKeepingUnitId), x => x.Id);

        var putawayService = PutawayService(database);
        var firstPutaway = new PutawayLicensePlateNumberCommand(
            firstLpnId, itemIds[(firstLpnId, bolts.Id)], firstDestination.Id, 40m);
        var firstPutawayContext = Command("worker-a");
        Value(await putawayService.PutawayAsync(firstPutaway, firstPutawayContext));
        Value(await putawayService.PutawayAsync(firstPutaway, firstPutawayContext));

        var boltSelection = Value(await putawayService.GetSelectionAsync(
            secondLpnId, itemIds[(secondLpnId, bolts.Id)]));
        Assert.Contains(boltSelection.Recommendations, x =>
            x.StorageLocationId == firstDestination.Id
            && x.Category == LicensePlateNumberPutawayRecommendationCategory.CurrentSkuStock);

        Value(await putawayService.PutawayAsync(
            new(secondLpnId, itemIds[(secondLpnId, bolts.Id)], firstDestination.Id, 30m),
            Command("worker-b")));
        Value(await putawayService.PutawayAsync(
            new(secondLpnId, itemIds[(secondLpnId, bolts.Id)], secondDestination.Id, 30m),
            Command("worker-b")));
        Value(await putawayService.PutawayAsync(
            new(secondLpnId, itemIds[(secondLpnId, nuts.Id)], secondDestination.Id, 20m),
            Command("worker-b")));

        var transferService = new InventoryTransferCommandService(
            new CommandExecutor(database),
            new InventoryPostingService(NullLogger<InventoryPostingService>.Instance));
        var transferId = Value(await transferService.CreateAsync(
            new(warehouse.Id, null), Command("picker")));
        Value(await transferService.MoveDirectAsync(
            new(transferId, firstDestination.Id, secondDestination.Id, bolts.Id, 10m),
            Command("picker")));

        var firstDetails = Value(await putawayService.GetAsync(firstLpnId));
        var secondDetails = Value(await putawayService.GetAsync(secondLpnId));
        Assert.True(firstDetails.IsCompleted);
        Assert.True(secondDetails.IsCompleted);
        var allItems = firstDetails.Items.Concat(secondDetails.Items).ToList();
        var allHistory = firstDetails.History.Concat(secondDetails.History).ToList();
        Assert.Equal(100m, allItems.Where(x => x.StockKeepingUnitId == bolts.Id).Sum(x => x.AcceptedQuantity));
        Assert.Equal(20m, allItems.Where(x => x.StockKeepingUnitId == nuts.Id).Sum(x => x.AcceptedQuantity));
        Assert.All(allItems, x => Assert.Equal(0m, x.RemainingQuantity));
        Assert.Equal(100m, allHistory.Where(x => x.StockKeepingUnitId == bolts.Id).Sum(x => x.Quantity));
        Assert.Equal(20m, allHistory.Where(x => x.StockKeepingUnitId == nuts.Id).Sum(x => x.Quantity));
        Assert.All(firstDetails.History, x => Assert.Equal("worker-a", x.CreatedBy));
        Assert.All(secondDetails.History, x => Assert.Equal("worker-b", x.CreatedBy));
        Assert.Equal(4, allHistory.Count);

        await using var verify = database.CreateDbContext();
        Assert.Equal(0m, (await verify.InventoryBalances.SingleAsync(x =>
            x.StorageLocationId == receivingLocation.Id && x.StockKeepingUnitId == bolts.Id)).Quantity);
        Assert.Equal(0m, (await verify.InventoryBalances.SingleAsync(x =>
            x.StorageLocationId == receivingLocation.Id && x.StockKeepingUnitId == nuts.Id)).Quantity);
        Assert.Equal(60m, (await verify.InventoryBalances.SingleAsync(x =>
            x.StorageLocationId == firstDestination.Id && x.StockKeepingUnitId == bolts.Id)).Quantity);
        Assert.Equal(40m, (await verify.InventoryBalances.SingleAsync(x =>
            x.StorageLocationId == secondDestination.Id && x.StockKeepingUnitId == bolts.Id)).Quantity);
        Assert.Equal(20m, (await verify.InventoryBalances.SingleAsync(x =>
            x.StorageLocationId == secondDestination.Id && x.StockKeepingUnitId == nuts.Id)).Quantity);
        Assert.Equal(4, await verify.LicensePlateNumberPutaways.CountAsync(x =>
            x.LicensePlateNumberId == firstLpnId || x.LicensePlateNumberId == secondLpnId));
        Assert.Equal(1, await verify.CommandReceipts.CountAsync(x =>
            x.RequestId == completionContext.RequestId));
        Assert.Equal(1, await verify.CommandReceipts.CountAsync(x =>
            x.RequestId == firstPutawayContext.RequestId));
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

    private static StorageLocation RecommendationLocation(
        Guid warehouseId,
        Zone zone,
        int number,
        string code,
        long? pickSequence) =>
        Value(StorageLocation.Create(
            Guid.NewGuid(), warehouseId, zone.Id, null, $"{zone.Code}{code}",
            Value(StorageLocationDetails.Create(
                $"Location {code}", false, LocationDimensions.Empty, LocationCoordinates.Empty, pickSequence))));

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

    private sealed record NewCloseState(
        Guid WarehouseId, Guid OrderId, Guid LocationId, Guid SkuId, Guid LpnId);

    private async Task<NewCloseState> SeedNewOpenReceivingLpnAsync()
    {
        var now = DateTimeOffset.UtcNow;
        var warehouse = new Warehouse { Id = Guid.NewGuid(), Name = "New close safety" };
        var sku = new StockKeepingUnit { Id = Guid.NewGuid(), Name = "Close SKU" };
        var zone = Zone(warehouse.Id, "NC", ZoneType.Receiving);
        var location = Location(warehouse.Id, zone, 1);
        var order = Value(ReceivingOrder.CreateNew(warehouse.Id, now));
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
                lpn, sku.Id, null, 3m, "close-test", now, false));
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
