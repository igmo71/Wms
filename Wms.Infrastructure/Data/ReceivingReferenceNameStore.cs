using Microsoft.EntityFrameworkCore;
using Wms.Application.Receiving;

namespace Wms.Data;

public sealed class ReceivingReferenceNameStore(
    IDbContextFactory<ApplicationDbContext> contextFactory) : IReceivingReferenceNames
{
    public async Task<Wms.Application.Receiving.ReceivingReferenceNames> GetAsync(
        IReadOnlyCollection<Guid> warehouseIds, IReadOnlyCollection<Guid> skuIds,
        IReadOnlyCollection<Guid> partnerIds, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var warehouses = await db.Warehouses.AsNoTracking()
            .Where(x => warehouseIds.Contains(x.Id) && x.Name != null && x.Name != "")
            .Select(x => new { x.Id, x.Name }).ToDictionaryAsync(x => x.Id, x => x.Name!, ct);
        var skus = await db.StockKeepingUnits.AsNoTracking()
            .Where(x => skuIds.Contains(x.Id) && x.Name != null && x.Name != "")
            .Select(x => new { x.Id, x.Name }).ToDictionaryAsync(x => x.Id, x => x.Name!, ct);
        var partners = await db.Partners.AsNoTracking()
            .Where(x => partnerIds.Contains(x.Id) && x.Name != null && x.Name != "")
            .Select(x => new { x.Id, x.Name }).ToDictionaryAsync(x => x.Id, x => x.Name!, ct);
        return new Wms.Application.Receiving.ReceivingReferenceNames(warehouses, skus, partners);
    }
}
