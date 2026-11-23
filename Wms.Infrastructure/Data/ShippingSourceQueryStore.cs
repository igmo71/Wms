using Microsoft.EntityFrameworkCore;
using Wms.Application.Picking;
using Wms.Domain.Enums;

namespace Wms.Data;

public sealed class ShippingSourceQueryStore(
    IDbContextFactory<ApplicationDbContext> contextFactory) : IShippingSourceQueryStore
{
    public async Task<IReadOnlyList<ShippingSourceListItem>> ListAsync(CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var sources = await db.ShippingOrders.AsNoTracking()
            .Include(x => x.Warehouse)
            .OrderByDescending(x => x.Date).ThenByDescending(x => x.Id)
            .ToArrayAsync(ct);
        if (sources.Length == 0) return [];

        var sourceIds = sources.Select(x => x.Id).ToArray();
        var partnerIds = sources.Where(x => x.ReceiverType == PartyType.Partner)
            .Select(x => x.ReceiverId).Distinct().ToArray();
        var individualIds = sources.Where(x => x.ReceiverType == PartyType.Individual)
            .Select(x => x.ReceiverId).Distinct().ToArray();
        var unitIds = sources.Where(x => x.ReceiverType == PartyType.OrganizationalUnit)
            .Select(x => x.ReceiverId).Distinct().ToArray();
        var receiverWarehouseIds = sources.Where(x => x.ReceiverType == PartyType.Warehouse)
            .Select(x => x.ReceiverId).Distinct().ToArray();
        var partners = await db.Partners.AsNoTracking()
            .Where(x => partnerIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var individuals = await db.Individuals.AsNoTracking()
            .Where(x => individualIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var units = await db.OrganizationalUnits.AsNoTracking()
            .Where(x => unitIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var receiverWarehouses = await db.Warehouses.AsNoTracking()
            .Where(x => receiverWarehouseIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var lines = await db.ShippingOrderItems.AsNoTracking()
            .Where(x => sourceIds.Contains(x.ShippingOrderId))
            .OrderBy(x => x.LineNumber)
            .Select(x => new { x.ShippingOrderId, Line = new ShippingSourceLineView(
                x.LineNumber, x.StockKeepingUnitId, x.StockKeepingUnit!.Code,
                x.StockKeepingUnit.Name, x.PlanQuantity) })
            .ToArrayAsync(ct);
        var linesBySource = lines.GroupBy(x => x.ShippingOrderId)
            .ToDictionary(x => x.Key,
                x => (IReadOnlyList<ShippingSourceLineView>)x.Select(y => y.Line).ToArray());
        var links = await db.PickingAssemblySourceLinks.AsNoTracking()
            .Where(x => x.DetachedAtUtc == null && sourceIds.Contains(x.ShippingOrderId))
            .Select(x => new { x.ShippingOrderId, x.PickingAssemblyId,
                AssemblyNumber = x.PickingAssembly!.Number })
            .ToArrayAsync(ct);
        var linksBySource = links.ToDictionary(x => x.ShippingOrderId);

        return sources.Select(source =>
        {
            linksBySource.TryGetValue(source.Id, out var link);
            return new ShippingSourceListItem(source.Id, source.Number, source.Date,
                source.Status, source.Queue, source.Posted, source.DeletionMark,
                source.WarehouseId, source.Warehouse?.Name,
                source.ReceiverType switch
                {
                    PartyType.Partner => partners.GetValueOrDefault(source.ReceiverId),
                    PartyType.Individual => individuals.GetValueOrDefault(source.ReceiverId),
                    PartyType.OrganizationalUnit => units.GetValueOrDefault(source.ReceiverId),
                    PartyType.Warehouse => receiverWarehouses.GetValueOrDefault(source.ReceiverId),
                    _ => null
                },
                source.LastSourceCheckAtUtc, source.LastSourceCheckError,
                link?.PickingAssemblyId, link?.AssemblyNumber,
                linesBySource.GetValueOrDefault(source.Id, []));
        }).ToArray();
    }
}
