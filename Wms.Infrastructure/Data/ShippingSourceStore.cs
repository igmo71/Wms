using System.Data;
using Microsoft.EntityFrameworkCore;
using Wms.Application.Picking;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Data;

public sealed class ShippingSourceStore(IDbContextFactory<ApplicationDbContext> contextFactory)
    : IShippingSourceStore
{
    public async Task<OperationResult<Guid>> SaveAsync(
        ShippingOrderImportSnapshot source, string rawDocumentJson,
        IReadOnlyDictionary<Guid, decimal> planBySku,
        DateTimeOffset checkedAtUtc, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, ct);

        var skuIds = source.Items.Select(x => x.StockKeepingUnitId)
            .Concat(source.BaseItems.Select(x => x.StockKeepingUnitId))
            .Distinct().ToArray();
        var knownSkuIds = await db.StockKeepingUnits.AsNoTracking()
            .Where(x => skuIds.Contains(x.Id)).Select(x => x.Id).ToArrayAsync(ct);
        if (knownSkuIds.Length != skuIds.Length)
            return OperationError.NotFound("Один или несколько SKU расходного документа 1С отсутствуют в WMS.");

        var order = await db.ShippingOrders
            .Include(x => x.Items).Include(x => x.BaseItems)
            .SingleOrDefaultAsync(x => x.Id == source.Id, ct);
        if (order is null)
        {
            var created = ShippingOrder.Create(source, checkedAtUtc);
            if (!created.IsSuccess)
                return created.Error!;
            order = created.Value!;
            db.ShippingOrders.Add(order);
        }
        else
        {
            var applied = order.ApplyCurrentSource(source, checkedAtUtc);
            if (!applied.IsSuccess)
                return applied.Error!;
        }

        var recorded = order.RecordSourceCheck(rawDocumentJson, null, checkedAtUtc);
        if (!recorded.IsSuccess)
            return recorded.Error!;

        var link = await db.PickingAssemblySourceLinks.AsNoTracking()
            .Where(x => x.ShippingOrderId == source.Id && x.DetachedAtUtc == null)
            .Select(x => new { x.PickingAssemblyId })
            .SingleOrDefaultAsync(ct);
        if (link is not null)
        {
            var assembly = await db.PickingAssemblies
                .Include(x => x.SourceLinks).Include(x => x.PlanLines)
                .SingleAsync(x => x.Id == link.PickingAssemblyId, ct);
            if (assembly.Status == PickingAssemblyStatus.Draft
                && assembly.WarehouseId == source.WarehouseId)
            {
                var existingSkuIds = assembly.PlanLines
                    .Where(x => x.ShippingOrderId == source.Id)
                    .Select(x => x.StockKeepingUnitId).ToHashSet();
                var refreshed = assembly.RefreshDraftSourcePlan(
                    source.Id, rawDocumentJson, planBySku);
                if (!refreshed.IsSuccess)
                    return refreshed.Error!;
                db.PickingAssemblyPlanLines.AddRange(assembly.PlanLines.Where(x =>
                    x.ShippingOrderId == source.Id
                    && !existingSkuIds.Contains(x.StockKeepingUnitId)));
            }
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return source.Id;
    }

    public async Task RecordFailureAsync(Guid refKey, string? rawDocumentJson,
        string error, DateTimeOffset checkedAtUtc, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var order = await db.ShippingOrders.SingleOrDefaultAsync(x => x.Id == refKey, ct);
        if (order is null)
            return;

        var recorded = order.RecordSourceCheck(rawDocumentJson,
            error.Length <= 2000 ? error : error[..2000], checkedAtUtc);
        if (recorded.IsSuccess)
            await db.SaveChangesAsync(ct);
    }
}
