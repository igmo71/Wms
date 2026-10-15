using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Wms.Application.Receiving;
using Wms.Common;
using Wms.Domain;

namespace Wms.Data;

public sealed class ReceivingOrderStore(IDbContextFactory<ApplicationDbContext> contextFactory)
    : IReceivingOrderStore
{
    public async Task<IReadOnlyList<ReceivingSourceState>> LoadSourcesAsync(
        IReadOnlyCollection<Guid> refKeys, CancellationToken ct)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        var sources = await context.ReceivingSourceDocuments.AsNoTracking()
            .Where(x => refKeys.Contains(x.RefKey)).ToListAsync(ct);
        return await WithVersionsAsync(context, sources, ct);
    }

    public async Task<IReadOnlyList<ReceivingSourceState>> ListUnlinkedSourcesAsync(
        CancellationToken ct)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        var sources = await context.ReceivingSourceDocuments.AsNoTracking()
            .Where(source => !context.ReceivingOrderSourceLinks.Any(link =>
                link.ReceivingSourceDocumentId == source.Id && link.DetachedAtUtc == null))
            .OrderByDescending(x => x.LastSuccessfulCheckAtUtc)
            .ThenBy(x => x.RefKey)
            .ToListAsync(ct);
        return await WithVersionsAsync(context, sources, ct);
    }

    public async Task<OperationResult<Guid>> CreateAsync(
        ReceivingOrder order, IReadOnlyList<ReceivingOrderSourceLink> links,
        IReadOnlyList<ReceivingOrderItem> items,
        IReadOnlyList<ReceivingSourceExpectation> expectedSources,
        CancellationToken ct)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, ct);

        var sourceIds = expectedSources.Select(x => x.SourceId).ToArray();
        var activeLinks = await context.ReceivingOrderSourceLinks.AsNoTracking()
            .Where(x => sourceIds.Contains(x.ReceivingSourceDocumentId)
                && x.DetachedAtUtc == null)
            .ToListAsync(ct);
        if (activeLinks.Count > 0)
        {
            var existingOrderId = activeLinks[0].ReceivingOrderId;
            if (activeLinks.Count == sourceIds.Length
                && activeLinks.All(x => x.ReceivingOrderId == existingOrderId))
            {
                var existingSourceIds = await context.ReceivingOrderSourceLinks.AsNoTracking()
                    .Where(x => x.ReceivingOrderId == existingOrderId && x.DetachedAtUtc == null)
                    .Select(x => x.ReceivingSourceDocumentId).ToListAsync(ct);
                if (existingSourceIds.Count == sourceIds.Length
                    && existingSourceIds.All(sourceIds.Contains))
                    return existingOrderId;
            }
            return OperationError.Conflict("Один из источников уже привязан к другому ордеру.");
        }

        var sources = await context.ReceivingSourceDocuments.AsNoTracking()
            .Where(x => sourceIds.Contains(x.Id)).ToListAsync(ct);
        if (sources.Count != expectedSources.Count
            || expectedSources.Any(expected => !sources.Any(source =>
                source.Id == expected.SourceId
                && source.LastSuccessfulVersionId == expected.VersionId
                && source.LastSuccessfulCheckAtUtc == expected.CheckedAtUtc)))
            return OperationError.Conflict("Источник изменился после просмотра; повторите создание ордера.");

        if (!await context.Warehouses.AnyAsync(x => x.Id == order.WarehouseId, ct))
            return OperationError.NotFound("Склад источников не найден в WMS.");

        context.ReceivingOrders.Add(order);
        context.ReceivingOrderSourceLinks.AddRange(links);
        context.ReceivingOrderItems.AddRange(items);
        try
        {
            await context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return order.Id;
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            return OperationError.Conflict("Источник уже был привязан или строка ордера создана конкурентно.");
        }
    }

    private static async Task<IReadOnlyList<ReceivingSourceState>> WithVersionsAsync(
        ApplicationDbContext context,
        IReadOnlyList<ReceivingSourceDocument> sources, CancellationToken ct)
    {
        var versionIds = sources.Select(x => x.LastSuccessfulVersionId)
            .OfType<Guid>().ToArray();
        var versions = await context.ReceivingSourceVersions.AsNoTracking()
            .Include(x => x.Lines)
            .Where(x => versionIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);
        return sources.Select(source => new ReceivingSourceState(source,
            source.LastSuccessfulVersionId is Guid versionId
                ? versions.GetValueOrDefault(versionId) : null)).ToArray();
    }
}
