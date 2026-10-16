using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
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

    internal sealed class Session(ApplicationDbContext db) : ICommandExecutionSession
    {
        internal ApplicationDbContext Db => db;

        public void Stage<TEntity>(TEntity entity) where TEntity : class => db.Add(entity);

        public async Task CommitAsync(
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
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException exception) when (IsStockConflict(exception))
            {
                throw new CommandCommitConflictException(
                    "Складское состояние изменилось. Обновите данные и повторите операцию.", exception);
            }
        }

        public ValueTask DisposeAsync() => db.DisposeAsync();

        private static bool IsStockConflict(DbUpdateException exception)
        {
            if (exception is DbUpdateConcurrencyException concurrency)
                return concurrency.Entries.Any(x => x.Entity is InventoryBalance
                    or StorageLocation or LicensePlateNumber or ReceivingOrder
                    or ReceivingOrderItem or StockKeepingUnit);

            for (Exception? current = exception; current is not null; current = current.InnerException)
            {
                if (current is SqlException { Number: 2601 or 2627 } sql
                    && (sql.Message.Contains(DatabaseObjectNames.InventoryBalancesBusinessKeyIndex,
                            StringComparison.Ordinal)
                        || sql.Message.Contains(DatabaseObjectNames.InventoryMovementsReversedMovementIndex,
                            StringComparison.Ordinal)
                        || sql.Message.Contains(DatabaseObjectNames.ReceivingOrderItemsPrimaryKey,
                            StringComparison.Ordinal)
                        || sql.Message.Contains(DatabaseObjectNames.ReceivingCycleParticipantIndex,
                            StringComparison.Ordinal)))
                    return true;
            }

            return false;
        }
    }
}
