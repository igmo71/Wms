using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Wms.Application.CommandExecution;
using Wms.Common;
using Wms.Data;
using Wms.Domain;
using Wms.Domain.Enums;
using static Wms.Tests.Infrastructure.TestSupport;

namespace Wms.Tests.Infrastructure;

[Collection(SqlDatabaseCollection.Name)]
public sealed class CommandExecutorTests(SqlDatabase database)
{
    [Fact]
    public async Task Ef_model_matches_the_migrations()
    {
        await using var db = database.CreateDbContext();
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task Replay_returns_the_original_result_and_changed_payload_conflicts()
    {
        var executor = new CommandExecutor(new CommandExecutionStore(database));
        var request = Command("command-replay");
        var firstHash = CommandExecutor.ComputeHash("payload-a");
        var changedHash = CommandExecutor.ComputeHash("payload-b");
        var calls = 0;
        var resourceId = Guid.NewGuid();

        Task<OperationResult<CommandExecutionResult>> Operation(ICommandExecutionContext session, CancellationToken _)
        {
            calls++;
            var sku = new StockKeepingUnit { Id = Guid.NewGuid(), Name = "Replay audit target" };
            session.Stage(new Warehouse { Id = resourceId, Name = "Replay effect" });
            session.Stage(sku);
            return Task.FromResult<OperationResult<CommandExecutionResult>>(new CommandExecutionResult(
                resourceId, [Audit("test.command", request.UserId, request.RequestId, sku.Id)]));
        }

        var first = await executor.ExecuteAsync("test.command", request.RequestId, firstHash, request.UserId, Operation, default);
        var replay = await executor.ExecuteAsync("test.command", request.RequestId, firstHash, request.UserId, Operation, default);
        var conflict = await executor.ExecuteAsync("test.command", request.RequestId, changedHash, request.UserId, Operation, default);

        Assert.Equal(resourceId, Value(first));
        Assert.Equal(resourceId, Value(replay));
        Error(conflict, OperationErrorType.Conflict);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Concurrent_duplicate_commits_one_effect_and_one_receipt()
    {
        const string marker = "Command race ";
        var request = Command("command-race");
        var requestHash = CommandExecutor.ComputeHash("same payload");
        var executor = new CommandExecutor(new CommandExecutionStore(database.WithInterceptor(new ReceiptSaveBarrier())));

        async Task<OperationResult<CommandExecutionResult>> Operation(ICommandExecutionContext session, CancellationToken ct)
        {
            var id = Guid.NewGuid();
            var sku = new StockKeepingUnit { Id = Guid.NewGuid(), Name = "Race audit " + id };
            session.Stage(new Warehouse { Id = id, Name = marker + id });
            session.Stage(sku);
            await Task.Yield();
            return new CommandExecutionResult(id,
                [Audit("test.concurrent", request.UserId, request.RequestId, sku.Id)]);
        }

        var results = await Task.WhenAll(
            executor.ExecuteAsync("test.concurrent", request.RequestId, requestHash, request.UserId, Operation, default),
            executor.ExecuteAsync("test.concurrent", request.RequestId, requestHash, request.UserId, Operation, default));

        Assert.Equal(Value(results[0]), Value(results[1]));
        await using var verify = database.CreateDbContext();
        Assert.Equal(1, await verify.Warehouses.CountAsync(x => x.Name != null && x.Name.StartsWith(marker)));
        Assert.Equal(1, await verify.CommandReceipts.CountAsync(x =>
            x.UserId == request.UserId && x.CommandType == "test.concurrent" && x.RequestId == request.RequestId));
        Assert.Equal(1, await verify.WarehouseAuditEvents.CountAsync(x =>
            x.CommandUserId == request.UserId && x.CommandType == "test.concurrent" && x.RequestId == request.RequestId));
    }

    [Fact]
    public async Task Failed_audit_rolls_back_stock_and_retry_commits_one_complete_set()
    {
        const string commandType = "test.stock-with-audit";
        var request = Command("stock-audit");
        var requestHash = CommandExecutor.ComputeHash("receipt 5");
        var warehouse = new Warehouse { Id = Guid.NewGuid(), Name = "Stock audit" };
        var sku = new StockKeepingUnit { Id = Guid.NewGuid(), Name = "Audited SKU" };
        var zone = Zone(warehouse.Id, "AU", ZoneType.Receiving);
        var location = Location(warehouse.Id, zone, 1);
        await using (var seed = database.CreateDbContext())
        {
            seed.AddRange(warehouse, sku, zone, location);
            await seed.SaveChangesAsync();
        }

        var executor = new CommandExecutor(new CommandExecutionStore(database));
        var calls = 0;
        var failAudit = true;

        Task<OperationResult<CommandExecutionResult>> Operation(ICommandExecutionContext session, CancellationToken _)
        {
            calls++;
            var now = DateTimeOffset.UtcNow;
            var movement = Value(InventoryMovement.Create(
                Guid.NewGuid(), warehouse.Id, null, location.Id, sku.Id, 5m, now,
                RecorderType.Unknown, null, null));
            Success(movement.Post(now));
            var balance = Value(InventoryBalance.Create(
                Guid.NewGuid(), warehouse.Id, location.Id, sku.Id, 0m, now));
            var change = Value(balance.Adjust(5m, now));
            var turnover = Value(InventoryTurnover.Create(
                Guid.NewGuid(), warehouse.Id, location.Id, sku.Id, change, now, movement.Id));
            var audit = Value(WarehouseAuditEvent.Create(
                commandType, now, request.UserId, null,
                failAudit ? Guid.NewGuid() : null, null, sku.Id, null,
                "{\"quantity\":\"0\"}", "{\"quantity\":\"5\"}",
                [movement.Id], commandUserId: request.UserId,
                commandType: commandType, requestId: request.RequestId));
            session.Stage(movement);
            session.Stage(balance);
            session.Stage(turnover);
            return Task.FromResult<OperationResult<CommandExecutionResult>>(
                new CommandExecutionResult(movement.Id, [audit]));
        }

        await Assert.ThrowsAsync<DbUpdateException>(() => executor.ExecuteAsync(
            commandType, request.RequestId, requestHash, request.UserId, Operation, default));

        await using (var failed = database.CreateDbContext())
        {
            Assert.False(await failed.InventoryMovements.AnyAsync(x => x.WarehouseId == warehouse.Id));
            Assert.False(await failed.InventoryBalances.AnyAsync(x => x.WarehouseId == warehouse.Id));
            Assert.False(await failed.InventoryTurnovers.AnyAsync(x => x.WarehouseId == warehouse.Id));
            Assert.False(await failed.WarehouseAuditEvents.AnyAsync(x => x.StockKeepingUnitId == sku.Id));
            Assert.False(await failed.CommandReceipts.AnyAsync(x => x.RequestId == request.RequestId));
        }

        failAudit = false;
        var postedId = Value(await executor.ExecuteAsync(
            commandType, request.RequestId, requestHash, request.UserId, Operation, default));
        var replayId = Value(await executor.ExecuteAsync(
            commandType, request.RequestId, requestHash, request.UserId, Operation, default));

        Assert.Equal(postedId, replayId);
        Assert.Equal(2, calls);
        await using var verify = database.CreateDbContext();
        Assert.Equal(1, await verify.InventoryMovements.CountAsync(x => x.WarehouseId == warehouse.Id));
        Assert.Equal(5m, (await verify.InventoryBalances.SingleAsync(x => x.WarehouseId == warehouse.Id)).Quantity);
        Assert.Equal(1, await verify.InventoryTurnovers.CountAsync(x => x.WarehouseId == warehouse.Id));
        var savedAudit = await verify.WarehouseAuditEvents.SingleAsync(x => x.StockKeepingUnitId == sku.Id);
        Assert.Equal(JsonSerializer.Serialize(new[] { postedId }), savedAudit.MovementIdsJson);
        Assert.Equal(request.RequestId, savedAudit.RequestId);
        var receipt = await verify.CommandReceipts.SingleAsync(x => x.RequestId == request.RequestId);
        Assert.Equal(postedId, receipt.ResultResourceId);
    }

    private static WarehouseAuditEvent Audit(
        string commandType, string userId, Guid requestId, Guid skuId) =>
        Value(WarehouseAuditEvent.Create(
            commandType, DateTimeOffset.UtcNow, userId, null,
            null, null, skuId, null, null, null,
            commandUserId: userId, commandType: commandType, requestId: requestId));
}
