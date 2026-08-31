using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Wms.Common;
using Wms.Data;
using Wms.Domain;

namespace Wms.Application.Persistence;

internal static class PersistenceConflictClassifier
{
    private static readonly OperationError BalanceConflict = OperationError.Conflict(
        "Остаток изменился. Обновите данные и повторите операцию.");

    private static readonly OperationError TransferConflict = OperationError.Conflict(
        "Перемещение изменилось. Обновите данные и повторите операцию.");

    private static readonly OperationError InventoryCountConflict = OperationError.Conflict(
        "Инвентаризация изменилась. Обновите данные и повторите операцию.");

    private static readonly OperationError StorageLocationConflict = OperationError.Conflict(
        "Состояние ячейки изменилось. Обновите данные и повторите операцию.");

    private static readonly OperationError ReceivingOrderConflict = OperationError.Conflict(
        "Приходный ордер изменился. Обновите данные и повторите операцию.");

    public static bool TryClassify(DbUpdateException exception, out OperationError error)
    {
        if (exception is DbUpdateConcurrencyException concurrencyException)
        {
            if (concurrencyException.Entries.Any(x => x.Entity is InventoryBalance))
            {
                error = BalanceConflict;
                return true;
            }

            if (concurrencyException.Entries.Any(x => x.Entity is InventoryTransfer))
            {
                error = TransferConflict;
                return true;
            }

            if (concurrencyException.Entries.Any(x => x.Entity is InventoryCount))
            {
                error = InventoryCountConflict;
                return true;
            }

            if (concurrencyException.Entries.Any(x => x.Entity is StorageLocation))
            {
                error = StorageLocationConflict;
                return true;
            }

            if (concurrencyException.Entries.Any(x => x.Entity is ReceivingOrder))
            {
                error = ReceivingOrderConflict;
                return true;
            }
        }

        if (FindSqlException(exception) is { Number: 2601 or 2627 } sqlException)
        {
            if (sqlException.Message.Contains(
                    DatabaseObjectNames.InventoryBalancesBusinessKeyIndex,
                    StringComparison.Ordinal))
            {
                error = BalanceConflict;
                return true;
            }

            if (sqlException.Message.Contains(
                    DatabaseObjectNames.InventoryMovementsTransferLineIndex,
                    StringComparison.Ordinal))
            {
                error = TransferConflict;
                return true;
            }

            if (sqlException.Message.Contains(
                    DatabaseObjectNames.InventoryCountItemsSkuIndex,
                    StringComparison.Ordinal))
            {
                error = InventoryCountConflict;
                return true;
            }

            if (sqlException.Message.Contains(
                    DatabaseObjectNames.InventoryTransfersActiveTransitLocationIndex,
                    StringComparison.Ordinal))
            {
                error = OperationError.Conflict(
                    "Транзитная позиция уже назначена другому активному перемещению.");
                return true;
            }

            if (sqlException.Message.Contains(
                    DatabaseObjectNames.StorageLocationLocksPrimaryKey,
                    StringComparison.Ordinal))
            {
                error = StorageLocationConflict;
                return true;
            }
        }

        error = null!;
        return false;
    }

    private static SqlException? FindSqlException(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException sqlException)
            {
                return sqlException;
            }
        }

        return null;
    }
}
