using Microsoft.EntityFrameworkCore;
using Wms.Application.Authorization;
using Wms.Application.CommandExecution;
using Wms.Application.StockKeepingUnits;
using Wms.Common;
using Wms.Data;
using Wms.Domain;
using Wms.Tests.Infrastructure;
using static Wms.Tests.Infrastructure.TestSupport;

namespace Wms.Tests.Receiving;

[Collection(SqlDatabaseCollection.Name)]
public sealed class ExpirationTrackingTests(SqlDatabase database)
{
    [Fact]
    public async Task Manager_change_survives_single_and_batch_catalog_import_with_one_history_event()
    {
        var skuId = Guid.NewGuid();
        await using (var seed = database.CreateDbContext())
        {
            seed.StockKeepingUnits.Add(new StockKeepingUnit { Id = skuId, Name = "Source name" });
            await seed.SaveChangesAsync();
        }

        var tracking = new SetExpirationTracking(
            new CommandExecutor(new CommandExecutionStore(database)),
            new ExpirationTrackingStore(database),
            new WarehousePermission(new ManagerPermission()), TimeProvider.System);
        Error(await tracking.ExecuteAsync(new SetExpirationTrackingRequest(
            Guid.NewGuid(), skuId, true, "operator-test")), OperationErrorType.Conflict);
        var request = new SetExpirationTrackingRequest(Guid.NewGuid(), skuId, true, "manager-test");
        Assert.Equal(skuId, Value(await tracking.ExecuteAsync(request)));
        Assert.Equal(skuId, Value(await tracking.ExecuteAsync(request)));

        var import = new StockKeepingUnitService(database);
        await import.CreateOrUpdateAsync(new StockKeepingUnit
        {
            Id = skuId, Name = "Single import", Code = "S1"
        });
        await import.CreateOrUpdateBatchAsync([new StockKeepingUnit
        {
            Id = skuId, Name = "Batch import", Code = "S2"
        }]);

        await using var verify = database.CreateDbContext();
        var sku = await verify.StockKeepingUnits.SingleAsync(x => x.Id == skuId);
        Assert.True(sku.TracksExpirationDate);
        Assert.Equal("Batch import", sku.Name);
        Assert.Equal("S2", sku.Code);
        Assert.Equal(1, await verify.WarehouseAuditEvents.CountAsync(x =>
            x.StockKeepingUnitId == skuId
            && x.EventType == SetExpirationTracking.ChangedEventType));
        var change = Assert.Single(await tracking.ListHistoryAsync(skuId));
        Assert.Equal("manager-test", change.ActorName);
        Assert.False(change.Before);
        Assert.True(change.After);
    }

    private sealed class ManagerPermission : IWarehousePermissionStore
    {
        public Task<bool> HasManagerRoleAsync(string userId, CancellationToken ct) =>
            Task.FromResult(userId == "manager-test");

        public Task<bool> HasReceivingRoleAsync(string userId, CancellationToken ct) =>
            Task.FromResult(false);
    }
}
