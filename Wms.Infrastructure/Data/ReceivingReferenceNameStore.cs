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
        var warehouses = warehouseIds.Count == 0 ? new Dictionary<Guid, string>()
            : await db.Warehouses.AsNoTracking()
                .Where(x => warehouseIds.Contains(x.Id) && x.Name != null && x.Name != "")
                .Select(x => new { x.Id, x.Name }).ToDictionaryAsync(x => x.Id, x => x.Name!, ct);
        var skus = skuIds.Count == 0 ? new Dictionary<Guid, string>()
            : await db.StockKeepingUnits.AsNoTracking()
                .Where(x => skuIds.Contains(x.Id) && x.Name != null && x.Name != "")
                .Select(x => new { x.Id, x.Name }).ToDictionaryAsync(x => x.Id, x => x.Name!, ct);
        var partners = partnerIds.Count == 0 ? new Dictionary<Guid, string>()
            : await db.Partners.AsNoTracking()
                .Where(x => partnerIds.Contains(x.Id) && x.Name != null && x.Name != "")
                .Select(x => new { x.Id, x.Name }).ToDictionaryAsync(x => x.Id, x => x.Name!, ct);
        return new Wms.Application.Receiving.ReceivingReferenceNames(warehouses, skus, partners);
    }
}
