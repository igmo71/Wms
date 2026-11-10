using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Wms.Application.CommandExecution;
using Wms.Domain;

namespace Wms.Data;

internal static class InventoryLocationLock
{
    public static async Task<IReadOnlyDictionary<Guid, StorageLocation>> AcquireAsync(
        ApplicationDbContext db, IEnumerable<Guid> locationIds, CancellationToken ct)
    {
        try
        {
            if (db.Database.CurrentTransaction is null)
                await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            var locations = new Dictionary<Guid, StorageLocation>();
            foreach (var id in locationIds.Distinct().Order())
            {
                // A location predates its stock groups. The same anchor protects empty groups and holds.
                var location = await db.StorageLocations.FromSqlInterpolated(
                        $"SELECT * FROM [StorageLocations] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {id}")
                    .Include(x => x.Warehouse).Include(x => x.Zone).Include(x => x.ActiveLock)
                    .SingleOrDefaultAsync(ct);
                if (location is not null)
                    locations.Add(id, location);
            }
            return locations;
        }
        catch (SqlException exception) when (exception.Number == 1205)
        {
            throw new CommandCommitConflictException("Ячейка изменилась. Повторите операцию.", exception);
        }
    }
}
