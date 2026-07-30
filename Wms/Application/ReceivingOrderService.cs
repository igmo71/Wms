using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text;
using Wms.Common;
using Wms.Data;
using Wms.Domain;

namespace Wms.Application;

internal class ReceivingOrderService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    ILogger<ReceivingOrderService> logger)
{
    internal async Task Import(ReceivingOrder externalItem, CancellationToken ct)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var existsingItem = await dbContext.ReceivingOrders
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == externalItem.Id, ct);

        if (existsingItem is null)
        {
            await CreateAsync(externalItem, ct);
        }
        else if (existsingItem.StartedAtUtc is null)
        {
            await UpdateAsync(externalItem, ct);
        }
    }

    private static string GetConflictDetails(ReceivingOrder existingItem, ReceivingOrder externalItem)
    {
        var builder = new StringBuilder();

        if (existingItem.Status != externalItem.Status)
            builder.AppendLine($"Статус: {externalItem.Status.GetDisplayName()} ");

        if (existingItem.Items.Count != externalItem.Items.Count)
            builder.AppendLine($"Количество строк: {externalItem.Items.Count} ");

        for (int i = 1; i <= existingItem.Items.Count; i++)
        {
            if (externalItem.Items.ElementAtOrDefault(i) is null)
                builder.AppendLine($"Отсутствует строка: {i} ");
            else if (existingItem.Items[i].PlanQuantity != externalItem.Items[i].PlanQuantity)
                builder.AppendLine($"В строке: {i} плановое количество {externalItem.Items[i].PlanQuantity}");
        }

        return builder.ToString();
    }

    private static bool CheckExternaConflict(ReceivingOrder existingItem, ReceivingOrder externalItem)
    {
        var hasStateChanges =
            existingItem.Status != externalItem.Status ||
            existingItem.Items.Count != externalItem.Items.Count;

        var hasPlannedQuantityChanges = existingItem.Items.Any(existing =>
            externalItem.Items.Any(external =>
                external.ReceivingOrderId == existing.ReceivingOrderId &&
                external.LineNumber == existing.LineNumber &&
                external.PlanQuantity != existing.PlanQuantity));

        return hasStateChanges || hasPlannedQuantityChanges;
    }

    private async Task<ReceivingOrder> CreateAsync(ReceivingOrder item, CancellationToken ct)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var entity = dbContext.ReceivingOrders.Add(item).Entity;

        try
        {
            await dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning("{Source} {Id} {DbUpdateException}", nameof(CreateAsync), item.Id, ex.Message);

            await UpdateAsync(item, ct);
        }

        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("{Source} {@Entity}", nameof(CreateAsync), entity);

        return entity;
    }

    private async Task UpdateAsync(ReceivingOrder item, CancellationToken ct)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        dbContext.ReceivingOrders.Update(item);

        await dbContext.SaveChangesAsync(ct);
    }
}