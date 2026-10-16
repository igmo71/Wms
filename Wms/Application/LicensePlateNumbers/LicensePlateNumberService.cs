using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Wms.Application.Commands;
using Wms.Common;
using Wms.Data;
using Wms.Domain;

namespace Wms.Application.LicensePlateNumbers;

public sealed class LicensePlateNumberService(CommandExecutor executor, IDbContextFactory<ApplicationDbContext> factory)
{
    public Task<OperationResult<Guid>> IssueAsync(int quantity, CommandContext context, CancellationToken ct = default) =>
        executor.ExecuteAsync("lpn-label.issue-batch", context.RequestId,
            CommandExecutor.ComputeHash(quantity.ToString(CultureInfo.InvariantCulture)), context.UserId,
            (db, _) =>
            {
                var result = LicensePlateNumberBatch.Issue(quantity, context.UserId, DateTimeOffset.UtcNow);
                if (!result.IsSuccess)
                    return Task.FromResult<OperationResult<Guid>>(result.Error!);
                db.LicensePlateNumberBatches.Add(result.Value!);
                return Task.FromResult<OperationResult<Guid>>(result.Value!.Id);
            }, ct);

    public async Task<(IReadOnlyList<LicensePlateNumber> Items, int Total)> ListAsync(string? search, int skip, int take, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.LicensePlateNumbers.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(x => x.Code.Contains(search.Trim()));
        var total = await query.CountAsync(ct);
        var items = await query.Include(x => x.Batch).OrderByDescending(x => x.Code)
            .Skip(Math.Max(0, skip)).Take(Math.Clamp(take, 1, 100)).ToListAsync(ct);
        return (items, total);
    }

    // Printing only reads issued values; it never allocates numbers or changes availability.
    public async Task<IReadOnlyList<LicensePlateNumber>> GetForPrintAsync(Guid id, bool batch, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.LicensePlateNumbers.AsNoTracking().Include(x => x.Batch)
            .Where(x => batch ? x.BatchId == id : x.Id == id).OrderBy(x => x.Code).ToListAsync(ct);
    }
}
