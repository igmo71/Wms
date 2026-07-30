using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wms.Data;
using Wms.Domain;

namespace Wms.Application;

internal class ReceivingOrderService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    ILogger<ReceivingOrderService> logger)
{
    internal async Task CreateOrUpdateAsync(ReceivingOrder newItem, CancellationToken ct)
    {
        throw new NotImplementedException();
    }
}
