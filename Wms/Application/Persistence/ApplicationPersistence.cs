using Microsoft.EntityFrameworkCore;
using Wms.Common;
using Wms.Data;

namespace Wms.Application.Persistence;

internal static class ApplicationPersistence
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
            when (PersistenceConflictClassifier.TryClassify(exception, out var error))
        {
            return error;
        }
    }
}
