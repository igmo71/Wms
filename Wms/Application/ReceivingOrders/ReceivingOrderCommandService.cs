using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wms.Common;
using Wms.Data;
using Wms.Domain;
using Wms.Domain.Enums;
using Wms.Integration.OneS.Services;

namespace Wms.Application.ReceivingOrders;

public class ReceivingOrderCommandService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    Document_ПриходныйОрдерНаТовары_OutboundService outboundService,
    IOptions<WmsSettings> options,
    ILogger<ReceivingOrderCommandService> logger)
{
    private readonly WmsSettings _wmsSettings = options.Value;

    internal async Task CreateOrUpdateImporttedOrderAsync(ReceivingOrder externaOrder, CancellationToken ct = default)
    {
        var source = nameof(CreateOrUpdateImporttedOrderAsync);

        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("{Source} Start {externaOrderId} {@externaOrder}", source, externaOrder.Id, externaOrder);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var existsingOrder = await dbContext.ReceivingOrders
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == externaOrder.Id, ct);

        if (existsingOrder is null)
        {
            var entity = dbContext.ReceivingOrders.Add(externaOrder).Entity;
        }
        else if (existsingOrder.IsDataVersionDiffer(externaOrder.DataVersion))
        {
            if (logger.IsEnabled(LogLevel.Debug))
                logger.LogDebug("{Source} DataVersion differ {OrderId} {existsingDataVersion} {externaDataVersion}",
                    source, existsingOrder.Id, existsingOrder.DataVersion, externaOrder.DataVersion);

            if ((existsingOrder.Status == ReceivingOrderStatus.Pending && !_wmsSettings.AllowExternalUpdatePending) ||
                (existsingOrder.Status == ReceivingOrderStatus.InProcess && !_wmsSettings.AllowExternalUpdateInProcess) ||
                (existsingOrder.Status == ReceivingOrderStatus.Completed && !_wmsSettings.AllowExternalUpdateCompleted))
            {
                if (logger.IsEnabled(LogLevel.Debug))
                    logger.LogDebug("{Source} External Update Not Allow {OrderId}", source, existsingOrder.Id);
                return;
            }

            existsingOrder.Update(externaOrder);
        }

        await dbContext.SaveChangesAsync(ct);

        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("{Source} Ok {externaOrderId}", source, externaOrder.Id);
    }

    public async Task<bool> StartOrderAsync(Guid orderId, CancellationToken ct = default)
    {
        var source = nameof(StartOrderAsync);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("{Source} Start {orderId}", source, orderId);

        var outboundResult = await outboundService.StartOrderAsync(orderId, ct);

        if (outboundResult is null)
        {
            logger.LogError("{Source} Failed {orderId}", source, orderId);
            return false;
        }

        var existsingOrder = await dbContext.ReceivingOrders
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == orderId, ct);

        if (existsingOrder is null)
        {
            logger.LogError("{Source} Not Found {orderId}", source, orderId);
            return false;
        }

        existsingOrder.Status = outboundResult.Status;
        existsingOrder.StartedAtUtc = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(ct);

        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("{Source} Ok {orderId}", source, orderId);

        return true;
    }

    public async Task<bool> CompleteOrderAsync(Guid orderId, CancellationToken ct = default)
    {
        var source = nameof(CompleteOrderAsync);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var existingOrder = await dbContext.ReceivingOrders
            .FirstOrDefaultAsync(x => x.Id == orderId, ct);


        if (existingOrder is null)
        {
            logger.LogError("{Source} Order Not Found {orderId}", source, orderId);
            return false;
        }

        if (existingOrder.HasPlanFactDifference)
        {
            var updateOrderItemsResult = await outboundService.UpdateOrderItemsAsync(existingOrder.Id, existingOrder.Items, ct);

            if (updateOrderItemsResult is null)
            {
                logger.LogError("{Source} Update Order Items failed", source);
                return false;
            }
        }

        var outboundResult = await outboundService.CompleteOrderAsync(orderId, ct);

        if (outboundResult is null)
        {
            logger.LogError("{Source} Complete Order failed", source);
            return false;
        }

        // TODO: Обновление должно прилететь по нотификации,
        // но уже установлено StartedAt и UpdateOrderAsImportAsync не пропустит, надо проверять
        //await UpdateOrderAsImportAsync(outboundResult, ct); 

        return true;
    }

    public async Task<int> UpdateOrderItemFactQuantityAsync(
        Guid receivingOrderId,
        int lineNumber,
        double factQuantity,
        string? comment,
        CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var result = await dbContext.ReceivingOrderItems
            .Where(x => x.ReceivingOrderId == receivingOrderId && x.LineNumber == lineNumber)
            .ExecuteUpdateAsync(x => x
                .SetProperty(p => p.FactQuantity, factQuantity)
                .SetProperty(p => p.Comment, comment), ct);

        return result;
    }
}
