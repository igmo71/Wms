using Microsoft.EntityFrameworkCore;
using Wms.Application.Commands;
using Wms.Common;
using Wms.Data;
using Wms.Domain;
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
        var executor = new CommandExecutor(database);
        var request = Command("command-replay");
        var firstHash = CommandExecutor.ComputeHash("payload-a");
        var changedHash = CommandExecutor.ComputeHash("payload-b");
        var calls = 0;
        var resourceId = Guid.NewGuid();

        Task<OperationResult<Guid>> Operation(ApplicationDbContext db, CancellationToken _)
        {
            calls++;
            db.Warehouses.Add(new Warehouse { Id = resourceId, Name = "Replay effect" });
            return Task.FromResult<OperationResult<Guid>>(resourceId);
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
        var executor = new CommandExecutor(database.WithInterceptor(new ReceiptSaveBarrier()));

        async Task<OperationResult<Guid>> Operation(ApplicationDbContext db, CancellationToken ct)
        {
            var id = Guid.NewGuid();
            db.Warehouses.Add(new Warehouse { Id = id, Name = marker + id });
            await Task.Yield();
            return id;
        }

        var results = await Task.WhenAll(
            executor.ExecuteAsync("test.concurrent", request.RequestId, requestHash, request.UserId, Operation, default),
            executor.ExecuteAsync("test.concurrent", request.RequestId, requestHash, request.UserId, Operation, default));

        Assert.Equal(Value(results[0]), Value(results[1]));
        await using var verify = database.CreateDbContext();
        Assert.Equal(1, await verify.Warehouses.CountAsync(x => x.Name != null && x.Name.StartsWith(marker)));
        Assert.Equal(1, await verify.CommandReceipts.CountAsync(x =>
            x.UserId == request.UserId && x.CommandType == "test.concurrent" && x.RequestId == request.RequestId));
    }
}
