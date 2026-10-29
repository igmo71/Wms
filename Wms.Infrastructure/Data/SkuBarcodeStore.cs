using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Wms.Application.SkuBarcodes;
using Wms.Application.CommandExecution;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Data;

public sealed class SkuBarcodeStore(IDbContextFactory<ApplicationDbContext> contextFactory)
    : ISkuBarcodeMutationLock, ISkuBarcodeSynchronizationStore,
        IReceivingSkuBarcodeStore, ISkuBarcodeListStore,
        ISkuBarcodeRemovalStore
{
    private const string LockResource = "WMS.SkuBarcodes";

    public async Task<IAsyncDisposable> AcquireAsync(CancellationToken ct)
    {
        var db = await contextFactory.CreateDbContextAsync(ct);
        try
        {
            await db.Database.OpenConnectionAsync(ct);
            var result = new SqlParameter("@result", SqlDbType.Int)
            {
                Direction = ParameterDirection.Output
            };
            await db.Database.ExecuteSqlRawAsync(
                "EXEC @result = sp_getapplock @Resource = N'" + LockResource
                + "', @LockMode = N'Exclusive', @LockOwner = N'Session', @LockTimeout = 30000",
                [result], ct);
            if (result.Value is not int code || code < 0)
                throw new InvalidOperationException("Не удалось получить блокировку штрихкодов SKU.");

            return new LockLease(db);
        }
        catch
        {
            await db.DisposeAsync();
            throw;
        }
    }

    public async Task ReconcileAsync(Guid? skuId,
        IReadOnlyCollection<SkuBarcodePair> sourcePairs, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        IQueryable<SkuBarcode> scope = db.SkuBarcodes;
        if (skuId is Guid oneSkuId)
            scope = scope.Where(x => x.SkuId == oneSkuId);

        var current = await scope.ToListAsync(ct);
        var keyComparer = new PairComparer();
        var byPair = current.ToDictionary(
            x => new SkuBarcodePair(x.SkuId, x.Value!), keyComparer);
        var incoming = sourcePairs.Distinct(keyComparer).ToArray();
        var incomingKeys = incoming.ToHashSet(keyComparer);

        foreach (var existing in current)
        {
            if (existing.Source == SkuBarcodeSource.OneC
                && !incomingKeys.Contains(new SkuBarcodePair(existing.SkuId, existing.Value!)))
                db.SkuBarcodes.Remove(existing);
        }

        foreach (var pair in incoming)
        {
            if (byPair.TryGetValue(pair, out var existing))
            {
                existing.Source = SkuBarcodeSource.OneC;
            }
            else
            {
                db.SkuBarcodes.Add(new SkuBarcode
                {
                    SkuId = pair.SkuId,
                    Value = pair.Value,
                    Source = SkuBarcodeSource.OneC
                });
            }
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public Task<ReceivingOrder?> LoadOrderAsync(ICommandExecutionContext session,
        Guid orderId, CancellationToken ct) => Db(session).ReceivingOrders
            .Include(x => x.Cycles)
            .Include(x => x.Participants)
            .Include(x => x.Items)
            .AsSplitQuery()
            .SingleOrDefaultAsync(x => x.Id == orderId, ct);

    public async Task<bool> IsCurrentSkuAsync(ICommandExecutionContext session,
        Guid orderId, Guid skuId, CancellationToken ct)
    {
        var db = Db(session);
        var fromSource = await (from link in db.ReceivingOrderSourceLinks
            join source in db.ReceivingSourceDocuments
                on link.ReceivingSourceDocumentId equals source.Id
            join line in db.ReceivingSourceLineProjections
                on source.LastSuccessfulVersionId equals (Guid?)line.ReceivingSourceVersionId
            where link.ReceivingOrderId == orderId && link.DetachedAtUtc == null
                && line.StockKeepingUnitId == skuId
            select line.Id).AnyAsync(ct);
        if (fromSource)
            return true;
        return await db.LicensePlateNumbers
            .Where(x => x.ReceivingOrderId == orderId
                && x.Status == LicensePlateNumberStatus.Closed)
            .SelectMany(x => x.Items)
            .AnyAsync(x => x.StockKeepingUnitId == skuId && x.DeletedAtUtc == null, ct);
    }

    public async Task<IReadOnlyList<SkuBarcodeAssignment>> FindAssignmentsAsync(
        ICommandExecutionContext session, string value, CancellationToken ct) =>
        await Db(session).SkuBarcodes.AsNoTracking()
            .Where(x => x.Value == value)
            .Select(x => new SkuBarcodeAssignment(
                x.SkuId, x.Sku!.Name, x.Sku.Code))
            .ToArrayAsync(ct);

    public void GuardOrderState(ICommandExecutionContext session, ReceivingOrder order)
    {
        var db = Db(session);
        db.Entry(order).Property(x => x.OperationalRevision).IsModified = true;
    }

    public async Task<SkuBarcodeListPage> ListAsync(
        SkuBarcodeListQuery filter, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        IQueryable<SkuBarcode> visible = db.SkuBarcodes.AsNoTracking();
        if (!filter.IncludeInactive)
            visible = visible.Where(x => !x.Sku!.DeletionMark);

        // (SkuId, Value) is the primary key, so two rows for one value
        // necessarily belong to two distinct SKUs.
        var duplicateValues = visible.GroupBy(x => x.Value)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key);

        IQueryable<SkuBarcode> query = visible;
        if (filter.OnlyDuplicates)
            query = query.Where(x => duplicateValues.Contains(x.Value));
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search;
            query = query.Where(x => x.Value!.Contains(search)
                || x.Sku!.Name != null && x.Sku.Name.Contains(search)
                || x.Sku!.Code != null && x.Sku.Code.Contains(search));
        }

        var total = await query.CountAsync(ct);
        query = filter.SortBy switch
        {
            "SkuName" or "Sku.Name" => filter.SortDescending
                ? query.OrderByDescending(x => x.Sku!.Name)
                    .ThenBy(x => x.SkuId).ThenBy(x => x.Value)
                : query.OrderBy(x => x.Sku!.Name)
                    .ThenBy(x => x.SkuId).ThenBy(x => x.Value),
            "Value" => filter.SortDescending
                ? query.OrderByDescending(x => x.Value).ThenBy(x => x.SkuId)
                : query.OrderBy(x => x.Value).ThenBy(x => x.SkuId),
            _ => query.OrderBy(x => x.Sku!.Name)
                .ThenBy(x => x.SkuId).ThenBy(x => x.Value)
        };

        var rows = await query.Skip(filter.Skip).Take(filter.Take)
            .Select(x => new SkuBarcodeListRow(x.SkuId, x.Value!,
                x.Sku!.Name, x.Sku.Code, x.Source,
                duplicateValues.Contains(x.Value)))
            .ToArrayAsync(ct);
        return new SkuBarcodeListPage(rows, total);
    }

    public Task<SkuBarcode?> LoadForRemovalAsync(ICommandExecutionContext session,
        Guid skuId, string value, CancellationToken ct) =>
        Db(session).SkuBarcodes.SingleOrDefaultAsync(x =>
            x.SkuId == skuId && x.Value == value, ct);

    public void StageRemoval(ICommandExecutionContext session, SkuBarcode barcode) =>
        Db(session).SkuBarcodes.Remove(barcode);

    private static ApplicationDbContext Db(ICommandExecutionContext session) =>
        session is CommandExecutionStore.Session commandSession
            ? commandSession.Db
            : throw new InvalidOperationException("Операция со штрихкодом требует сессию команды.");

    private sealed class LockLease(ApplicationDbContext db) : IAsyncDisposable
    {
        // Closing the dedicated connection releases the session-owned SQL lock.
        public ValueTask DisposeAsync() => db.DisposeAsync();
    }

    private sealed class PairComparer : IEqualityComparer<SkuBarcodePair>
    {
        public bool Equals(SkuBarcodePair? left, SkuBarcodePair? right) =>
            left?.SkuId == right?.SkuId
            && StringComparer.OrdinalIgnoreCase.Equals(left?.Value, right?.Value);

        public int GetHashCode(SkuBarcodePair pair) =>
            HashCode.Combine(pair.SkuId,
                StringComparer.OrdinalIgnoreCase.GetHashCode(pair.Value));
    }
}
