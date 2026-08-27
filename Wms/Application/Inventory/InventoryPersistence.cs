using Microsoft.EntityFrameworkCore;
using Wms.Common;
using Wms.Data;

namespace Wms.Application.Inventory;

internal static class InventoryPersistence
{
    public static async Task<OperationResult> SaveChangesAsync(
        ApplicationDbContext dbContext,
        CancellationToken ct)
    {
        try
        {
            await dbContext.SaveChangesAsync(ct);
            return OperationResult.Success();
        }
        catch (DbUpdateException exception)
            when (InventoryPersistenceConflictClassifier.TryClassify(exception, out var error))
        {
            return error;
        }
    }
}
