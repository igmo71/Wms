using System.Data;
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

        internal async Task BeginSerializableAsync(CancellationToken ct)
        {
            if (db.Database.CurrentTransaction is not null)
                throw new InvalidOperationException("Транзакция команды уже открыта.");
            await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        }

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
                if (db.Database.CurrentTransaction is { } transaction)
                    await transaction.CommitAsync(ct);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                if (db.Database.CurrentTransaction is { } transaction)
                    await transaction.RollbackAsync(ct);
                if (exception is DbUpdateException update && IsKnownConflict(update)
                    || exception is SqlException { Number: 1205 })
                    throw new CommandCommitConflictException(
                        "Складское состояние изменилось. Обновите данные и повторите операцию.", exception);
                throw;
            }
        }

        public ValueTask DisposeAsync() => db.DisposeAsync();

        private static bool IsKnownConflict(DbUpdateException exception)
        {
            if (exception is DbUpdateConcurrencyException concurrency)
                return concurrency.Entries.Any(x => x.Entity is InventoryBalance
                    or StorageLocation or LicensePlateNumber or ReceivingOrder
                    or ReceivingOrderItem or StockKeepingUnit or ReceivingSourceDocument
                    or WarehouseOpeningImport or PickingReservation or StockShortage
                    or ReplenishmentTask);

            for (Exception? current = exception; current is not null; current = current.InnerException)
            {
                if (current is SqlException { Number: 1205 })
                    return true;
                if (current is SqlException { Number: 2601 or 2627 } sql
                    && (sql.Message.Contains(DatabaseObjectNames.WarehouseOpeningImportsWarehouseIndex,
                            StringComparison.Ordinal)
                        || sql.Message.Contains(DatabaseObjectNames.StorageLocationsWarehouseCodeIndex,
                            StringComparison.Ordinal)
                        || sql.Message.Contains(DatabaseObjectNames.ZonesWarehouseCodeIndex,
                            StringComparison.Ordinal)
                        || sql.Message.Contains(DatabaseObjectNames.InventoryBalancesBusinessKeyIndex,
                            StringComparison.Ordinal)
                        || sql.Message.Contains(DatabaseObjectNames.InventoryMovementsOpeningBalanceLineIndex,
                            StringComparison.Ordinal)
                        || sql.Message.Contains(DatabaseObjectNames.InventoryMovementsReversedMovementIndex,
                            StringComparison.Ordinal)
                        || sql.Message.Contains(DatabaseObjectNames.ReceivingOrderItemsPrimaryKey,
                            StringComparison.Ordinal)
                        || sql.Message.Contains(DatabaseObjectNames.ReceivingCycleParticipantIndex,
                            StringComparison.Ordinal)
                        || sql.Message.Contains(DatabaseObjectNames.ReceivingSourceActiveLinkIndex,
                            StringComparison.Ordinal)
                        || sql.Message.Contains(DatabaseObjectNames.StorageLocationLocksPrimaryKey,
                            StringComparison.Ordinal)
                        || sql.Message.Contains(DatabaseObjectNames.PickingReservationsPrimaryKey,
                            StringComparison.Ordinal)
                        || sql.Message.Contains(DatabaseObjectNames.StockShortagesActiveGroupIndex,
                            StringComparison.Ordinal)
                        || sql.Message.Contains(DatabaseObjectNames.ReplenishmentActiveSourceIndex,
                            StringComparison.Ordinal)
                        || sql.Message.Contains(DatabaseObjectNames.ReplenishmentTransferMovementIndex,
                            StringComparison.Ordinal)))
                    return true;
            }

            return false;
        }
    }
}
