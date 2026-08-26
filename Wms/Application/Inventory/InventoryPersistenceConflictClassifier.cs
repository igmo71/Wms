using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Wms.Common;
using Wms.Domain;

namespace Wms.Application.Inventory;

internal static class InventoryPersistenceConflictClassifier
{
    private const string BalanceBusinessKeyIndex = "UX_InventoryBalances_BusinessKey";
    private const string TransferLineIndex = "UX_InventoryMovements_TransferLine";
    private const string ActiveTransitLocationIndex =
        "IX_InventoryTransfers_TransitStorageLocationId";

    private static readonly OperationError BalanceConflict = OperationError.Conflict(
        "Остаток изменился. Обновите данные и повторите операцию.");

    private static readonly OperationError TransferConflict = OperationError.Conflict(
        "Перемещение изменилось. Обновите данные и повторите операцию.");

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
        }

        if (FindSqlException(exception) is { Number: 2601 or 2627 } sqlException)
        {
            if (sqlException.Message.Contains(BalanceBusinessKeyIndex, StringComparison.Ordinal))
            {
                error = BalanceConflict;
                return true;
            }

            if (sqlException.Message.Contains(TransferLineIndex, StringComparison.Ordinal))
            {
                error = TransferConflict;
                return true;
            }

            if (sqlException.Message.Contains(
                    ActiveTransitLocationIndex,
                    StringComparison.Ordinal))
            {
                error = OperationError.Conflict(
                    "Транзитная позиция уже назначена другому активному перемещению.");
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
