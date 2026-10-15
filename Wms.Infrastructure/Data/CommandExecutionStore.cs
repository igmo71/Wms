using Microsoft.EntityFrameworkCore;
using Wms.Application.CommandExecution;
using Wms.Domain;

namespace Wms.Data;

public sealed class CommandExecutionStore(IDbContextFactory<ApplicationDbContext> dbContextFactory)
    : ICommandExecutionStore
{
    public async Task<CommandReceiptSnapshot?> FindReceiptAsync(
        string userId, string commandType, Guid requestId, CancellationToken ct)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        return await db.CommandReceipts.AsNoTracking()
            .Where(x => x.UserId == userId && x.CommandType == commandType && x.RequestId == requestId)
            .Select(x => new CommandReceiptSnapshot(
                x.UserId, x.CommandType, x.RequestId, x.RequestHash, x.ResultResourceId))
            .SingleOrDefaultAsync(ct);
    }

    public async Task<ICommandExecutionSession> OpenSessionAsync(CancellationToken ct) =>
        new Session(await dbContextFactory.CreateDbContextAsync(ct));

    private sealed class Session(ApplicationDbContext db) : ICommandExecutionSession
    {
        public void Stage<TEntity>(TEntity entity) where TEntity : class => db.Add(entity);

        public Task CommitAsync(
            CommandReceiptSnapshot receipt,
            IReadOnlyCollection<WarehouseAuditEvent> auditEvents,
            CancellationToken ct)
        {
            db.CommandReceipts.Add(new CommandReceipt
            {
                UserId = receipt.UserId,
                CommandType = receipt.CommandType,
                RequestId = receipt.RequestId,
                RequestHash = receipt.RequestHash,
                ResultResourceId = receipt.ResultResourceId,
                CompletedAtUtc = DateTimeOffset.UtcNow
            });
            db.WarehouseAuditEvents.AddRange(auditEvents);
            // SaveChanges uses one database transaction for the staged effect, audit and receipt.
            return db.SaveChangesAsync(ct);
        }

        public ValueTask DisposeAsync() => db.DisposeAsync();
    }
}
