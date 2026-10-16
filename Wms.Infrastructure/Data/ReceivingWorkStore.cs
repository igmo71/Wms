using Microsoft.EntityFrameworkCore;
using Wms.Application.CommandExecution;
using Wms.Application.Receiving;
using Wms.Domain;

namespace Wms.Data;

public sealed class ReceivingWorkStore(IDbContextFactory<ApplicationDbContext> contextFactory)
    : IReceivingWorkStore
{
    public async Task<IReadOnlyList<Guid>> LoadSourceRefKeysAsync(Guid orderId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        return await (from link in db.ReceivingOrderSourceLinks
                join source in db.ReceivingSourceDocuments
                    on link.ReceivingSourceDocumentId equals source.Id
                where link.ReceivingOrderId == orderId && link.DetachedAtUtc == null
                select source.RefKey).Distinct().ToListAsync(ct);
    }

    public Task<ReceivingOrder?> LoadOrderAsync(
        ICommandExecutionContext session, Guid orderId, CancellationToken ct) =>
        Db(session).ReceivingOrders
            .Include(x => x.Cycles)
            .Include(x => x.Participants)
            .Include(x => x.Items)
            .Include(x => x.LicensePlateNumbers).ThenInclude(x => x.Items)
            .AsSplitQuery()
            .SingleOrDefaultAsync(x => x.Id == orderId, ct);

    public async Task<IReadOnlyList<ReceivingWorkSource>> LoadSourcesAsync(
        ICommandExecutionContext session, Guid orderId, CancellationToken ct)
    {
        var db = Db(session);
        return await (from link in db.ReceivingOrderSourceLinks
                join source in db.ReceivingSourceDocuments
                    on link.ReceivingSourceDocumentId equals source.Id
                join version in db.ReceivingSourceVersions
                    on source.LastSuccessfulVersionId equals (Guid?)version.Id into versions
                from version in versions.DefaultIfEmpty()
                where link.ReceivingOrderId == orderId && link.DetachedAtUtc == null
                select new ReceivingWorkSource(source.RefKey, source.LastSuccessfulVersionId,
                    version == null ? null : version.SnapshotJson, source.LastAttemptError))
            .ToListAsync(ct);
    }

    public Task<StorageLocation?> LoadLocationAsync(
        ICommandExecutionContext session, Guid locationId, CancellationToken ct) =>
        Db(session).StorageLocations.Include(x => x.Zone).Include(x => x.ActiveLock)
            .SingleOrDefaultAsync(x => x.Id == locationId, ct);

    public Task<LicensePlateNumber?> LoadLpnAsync(
        ICommandExecutionContext session, string code, CancellationToken ct) =>
        Db(session).LicensePlateNumbers.Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.Code == code, ct);

    public Task<LicensePlateNumber?> LoadLpnAsync(
        ICommandExecutionContext session, Guid lpnId, CancellationToken ct) =>
        Db(session).LicensePlateNumbers.Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.Id == lpnId, ct);

    public Task<StockKeepingUnit?> LoadSkuAsync(
        ICommandExecutionContext session, Guid skuId, CancellationToken ct) =>
        Db(session).StockKeepingUnits.SingleOrDefaultAsync(x => x.Id == skuId, ct);

    public void GuardClosingInputs(
        ICommandExecutionContext session, ReceivingOrderItem item, StockKeepingUnit sku)
    {
        var db = Db(session);
        if (db.Entry(item).State == EntityState.Detached
            || db.Entry(sku).State == EntityState.Detached)
            throw new InvalidOperationException("План и признак SKU должны быть загружены в сессии команды.");
        db.Entry(item).Property(x => x.CurrentPlanQuantity).IsModified = true;
        db.Entry(sku).Property(x => x.TracksExpirationDate).IsModified = true;
    }

    private static ApplicationDbContext Db(ICommandExecutionContext session) =>
        session is CommandExecutionStore.Session commandSession
            ? commandSession.Db
            : throw new InvalidOperationException("Работа с приёмкой требует сессию команды.");
}
