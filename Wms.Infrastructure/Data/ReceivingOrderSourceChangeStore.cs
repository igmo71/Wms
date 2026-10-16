using Microsoft.EntityFrameworkCore;
using Wms.Application.CommandExecution;
using Wms.Application.Receiving;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Data;

public sealed class ReceivingOrderSourceChangeStore : IReceivingOrderSourceChangeStore
{
    public async Task<ReceivingSourceChangeState> LoadAsync(
        ICommandExecutionContext session, Guid orderId, Guid sourceRefKey, CancellationToken ct)
    {
        var db = Db(session);
        var order = await db.ReceivingOrders.Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.Id == orderId, ct);
        if (order is null)
            return new(null, null, null, null, [], false);

        var links = await db.ReceivingOrderSourceLinks
            .Where(x => x.ReceivingOrderId == orderId && x.DetachedAtUtc == null)
            .ToArrayAsync(ct);
        var source = await db.ReceivingSourceDocuments
            .SingleOrDefaultAsync(x => x.RefKey == sourceRefKey, ct);
        var targetLink = source is null ? null : links.SingleOrDefault(x =>
            x.ReceivingSourceDocumentId == source.Id);
        var linkedOrderId = source is null ? null : await db.ReceivingOrderSourceLinks
            .Where(x => x.ReceivingSourceDocumentId == source.Id && x.DetachedAtUtc == null)
            .Select(x => (Guid?)x.ReceivingOrderId).SingleOrDefaultAsync(ct);

        var sourceIds = links.Select(x => x.ReceivingSourceDocumentId).ToArray();
        var activeDocuments = await db.ReceivingSourceDocuments
            .Where(x => sourceIds.Contains(x.Id)).ToArrayAsync(ct);
        ReceivingSourceDocument[] allDocuments = source is null || activeDocuments.Any(x => x.Id == source.Id)
            ? activeDocuments : [.. activeDocuments, source];
        var versionIds = allDocuments.Select(x => x.LastSuccessfulVersionId)
            .OfType<Guid>().ToArray();
        var versions = await db.ReceivingSourceVersions.AsNoTracking()
            .Include(x => x.Lines).Where(x => versionIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);
        ReceivingSourceState State(ReceivingSourceDocument document) => new(document,
            document.LastSuccessfulVersionId is Guid id
                ? versions.GetValueOrDefault(id) : null);

        var hasPostedLpn = await db.LicensePlateNumbers.AnyAsync(x =>
            x.ReceivingOrderId == orderId && (x.Status == LicensePlateNumberStatus.Closed
                || x.Status == LicensePlateNumberStatus.Deleted), ct);
        return new(order, source is null ? null : State(source), targetLink,
            linkedOrderId, activeDocuments.Select(State).ToArray(), hasPostedLpn);
    }

    public void GuardSources(ICommandExecutionContext session,
        IReadOnlyList<ReceivingSourceState> sources)
    {
        var db = Db(session);
        foreach (var source in sources)
        {
            var entry = db.Entry(source.Source);
            if (entry.State == EntityState.Detached)
                throw new InvalidOperationException("Источник должен быть загружен в сессии команды.");
            entry.Property(x => x.LastSuccessfulVersionId).IsModified = true;
        }
    }

    private static ApplicationDbContext Db(ICommandExecutionContext session) =>
        session is CommandExecutionStore.Session current
            ? current.Db
            : throw new InvalidOperationException("Нужна сессия команды WMS.");
}
