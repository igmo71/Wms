using Microsoft.EntityFrameworkCore;
using Wms.Application.Receiving;
using Wms.Domain;

namespace Wms.Data;

public sealed class ReceivingSkuStore(IDbContextFactory<ApplicationDbContext> contextFactory)
    : IReceivingSkuStore
{
    public async Task<bool> OrderExistsAsync(Guid orderId, CancellationToken ct)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        return await context.ReceivingOrders.AnyAsync(x => x.Id == orderId, ct);
    }

    public async Task<IReadOnlyList<ReceivingSkuCandidate>> SearchAsync(
        Guid orderId, string term, ReceivingSkuSearchScope scope, int take,
        CancellationToken ct)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        var query = context.StockKeepingUnits.AsNoTracking()
            .Where(sku => (sku.Name != null && sku.Name.Contains(term))
                    || (sku.Code != null && sku.Code.Contains(term))
                    || context.SkuBarcodes.Any(barcode =>
                        barcode.SkuId == sku.Id && barcode.Value == term));
        if (scope == ReceivingSkuSearchScope.Order)
            query = query.Where(sku => context.ReceivingOrderItems.Any(item =>
                item.ReceivingOrderId == orderId && item.StockKeepingUnitId == sku.Id));
        else
            query = query.Where(sku => !sku.DeletionMark);

        var skus = await query.OrderBy(x => x.Name).ThenBy(x => x.Code)
            .ThenBy(x => x.Id).Take(take).ToListAsync(ct);
        return await WithOrderStateAsync(context, orderId, skus, ct);
    }

    public async Task<IReadOnlyList<ReceivingSkuCandidate>> FindByBarcodeAsync(
        Guid orderId, string barcode, CancellationToken ct)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        var skus = await context.StockKeepingUnits.AsNoTracking()
            .Where(x => !x.DeletionMark && context.SkuBarcodes.Any(item =>
                item.SkuId == x.Id && item.Value == barcode))
            .OrderBy(x => x.Id).Take(2).ToListAsync(ct);
        return await WithOrderStateAsync(context, orderId, skus, ct);
    }

    public async Task<ReceivingSkuAddState> LoadAddStateAsync(
        Guid orderId, Guid skuId, CancellationToken ct)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);
        var orderExists = await context.ReceivingOrders.AnyAsync(x => x.Id == orderId, ct);
        var sku = await context.StockKeepingUnits.AsNoTracking()
            .Where(x => x.Id == skuId)
            .Select(x => new { x.DeletionMark }).SingleOrDefaultAsync(ct);
        var itemExists = await context.ReceivingOrderItems.AnyAsync(x =>
            x.ReceivingOrderId == orderId && x.StockKeepingUnitId == skuId, ct);
        return new ReceivingSkuAddState(orderExists, sku is not null,
            sku?.DeletionMark == true, itemExists);
    }

    private static async Task<IReadOnlyList<ReceivingSkuCandidate>> WithOrderStateAsync(
        ApplicationDbContext context, Guid orderId,
        IReadOnlyList<StockKeepingUnit> skus, CancellationToken ct)
    {
        var skuIds = skus.Select(x => x.Id).ToArray();
        var items = await context.ReceivingOrderItems.AsNoTracking()
            .Where(x => x.ReceivingOrderId == orderId && skuIds.Contains(x.StockKeepingUnitId))
            .ToDictionaryAsync(x => x.StockKeepingUnitId, ct);
        return skus.Select(sku =>
        {
            items.TryGetValue(sku.Id, out var item);
            return new ReceivingSkuCandidate(sku.Id, sku.Name, sku.Code,
                sku.TracksExpirationDate, item is not null,
                item?.CurrentPlanQuantity, item?.FactQuantity);
        }).ToArray();
    }
}
