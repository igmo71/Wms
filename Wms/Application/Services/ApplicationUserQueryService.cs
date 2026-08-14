using Microsoft.EntityFrameworkCore;
using Wms.Data;

namespace Wms.Application.Services;

public class ApplicationUserQueryService(IDbContextFactory<ApplicationDbContext> dbContextFactory)
{
    public async Task<IReadOnlyDictionary<string, string>> GetUserNamesAsync(
        IEnumerable<string?> userIds,
        CancellationToken ct = default)
    {
        var ids = userIds
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .Distinct()
            .ToArray();

        if (ids.Length == 0)
            return new Dictionary<string, string>();

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        return await dbContext.Users
            .AsNoTracking()
            .Where(x => ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.UserName ?? x.Id, ct);
    }
}
