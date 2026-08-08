using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wms.Common;
using Wms.Data;
using Wms.Domain;
using Wms.Integration.OneS.Services;

namespace Wms.Application.Services;

internal class ShippingOrderCommandService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    IOptions<WmsSettings> options,
    BalanceAndTurnoverService balanceAndTurnoverService,
    Document_РасходныйОрдерНаТовары_OutboundService outboundService,
    ILogger<ShippingOrderCommandService> logger)
{
    private readonly WmsSettings _wmsSettings = options.Value;

    internal async Task ImportOrderAsync(ShippingOrder externalOrder, CancellationToken ct)
    {
        using var scope = logger.BeginScope("ShippingOrder Import {OrderId}", externalOrder.Id);

        using var activity = AppTracing.StartActivity("ShippingOrder.Import", nameof(ShippingOrderCommandService));

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var existingOrder = await dbContext.ShippingOrders
            .Include(x => x.Items)
            .Include(x => x.BaseItems)
            .FirstOrDefaultAsync(x => x.Id == externalOrder.Id, ct);

        var now = DateTimeOffset.UtcNow;

        if (existingOrder is null)
        {
            if (!externalOrder.AllowExternalCreate(_wmsSettings))
            {
                logger.LogDebug("External document status is completed, new order create not allowed");

                return;
            }

            externalOrder.CreatedAtUtc = now;

            dbContext.ShippingOrders.Add(externalOrder);
        }
        else
        {
            var hasExternalChanges = existingOrder.HasExternalChanges(externalOrder);

            if (!hasExternalChanges)
            {
                logger.LogDebug("No external document changes detected");

                return;
            }

            if (!existingOrder.AllowExternalUpdate(_wmsSettings))
            // Что бы разрешить для статуса Complete, вероятно, потребуется доработка (откат BalanceAndTurnover...)
            {
                existingOrder.ExternalChangeDetected = true;

                logger.LogWarning("External document changes detected, order update not allowed");
            }
            else
            {
                existingOrder.UpdateOrder(externalOrder);

                existingOrder.UpdatedAtUtc = now;

                existingOrder.ExternalChangeDetected = false;
            }
        }

        await dbContext.SaveChangesAsync(ct);
    }
}
