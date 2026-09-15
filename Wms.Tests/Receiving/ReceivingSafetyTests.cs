using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Wms.Application.Commands;
using Wms.Application.Inventory.Movements;
using Wms.Application.LicensePlateNumbers;
using Wms.Application.ReceivingOrders;
using Wms.Common;
using Wms.Data;
using Wms.Domain;
using Wms.Domain.Enums;
using Wms.Tests.Infrastructure;
using static Wms.Tests.Infrastructure.TestSupport;

namespace Wms.Tests.Receiving;

[Collection(SqlDatabaseCollection.Name)]
public sealed class ReceivingSafetyTests(SqlDatabase database)
{
    [Fact]
    public async Task Maintenance_cleanup_never_reissues_an_lpn_code()
    {
        var isolated = new SqlDatabase();
        await isolated.InitializeAsync();
        try
        {
            var service = LpnService(isolated);
            var firstBatch = Value(await service.IssueAsync(1, Command("lpn-cleanup")));
            string lastCode;
            await using (var firstRead = isolated.CreateDbContext())
                lastCode = await firstRead.LicensePlateNumbers.Where(x => x.BatchId == firstBatch).Select(x => x.Code).SingleAsync();

            foreach (var scriptPath in new[] { "scripts/clear-wms-operational-data.sql", "scripts/clear-database-except-identity.sql" })
            {
                var fullScriptPath = Path.Combine(AppContext.BaseDirectory, scriptPath);
                await using (var cleanup = isolated.CreateDbContext())
                    await cleanup.Database.ExecuteSqlRawAsync(await File.ReadAllTextAsync(fullScriptPath));

                var nextBatch = Value(await service.IssueAsync(1, Command("lpn-cleanup")));
                await using var nextRead = isolated.CreateDbContext();
                var nextCode = await nextRead.LicensePlateNumbers.Where(x => x.BatchId == nextBatch).Select(x => x.Code).SingleAsync();
                Assert.True(string.CompareOrdinal(nextCode, lastCode) > 0);
                lastCode = nextCode;
            }
        }
        finally
        {
            await isolated.DisposeAsync();
        }
    }

    [Fact]
    public async Task Concurrent_lpn_issuance_is_unique_and_duplicate_request_is_once_only()
    {
        var duplicateContext = Command("lpn-race");
        var duplicateService = LpnService(database.WithInterceptor(new ReceiptSaveBarrier()));
        var duplicate = await Task.WhenAll(
            duplicateService.IssueAsync(20, duplicateContext),
            duplicateService.IssueAsync(20, duplicateContext));

        Assert.Equal(Value(duplicate[0]), Value(duplicate[1]));

        var independentService = LpnService(database.WithInterceptor(new ReceiptSaveBarrier()));
        var independent = await Task.WhenAll(
            independentService.IssueAsync(40, Command("lpn-independent-a")),
            independentService.IssueAsync(40, Command("lpn-independent-b")));

        Assert.NotEqual(Value(independent[0]), Value(independent[1]));
        await using var verify = database.CreateDbContext();
        Assert.Equal(20, await verify.LicensePlateNumbers.CountAsync(x => x.BatchId == Value(duplicate[0])));
        var independentIds = independent.Select(Value).ToArray();
        var codes = await verify.LicensePlateNumbers
            .Where(x => independentIds.Contains(x.BatchId))
            .Select(x => x.Code)
            .ToListAsync();
        Assert.Equal(80, codes.Count);
        Assert.Equal(codes.Count, codes.Distinct().Count());
    }

    [Fact]
    public async Task Concurrent_first_participants_leave_one_shared_receiving_location()
    {
        var warehouse = new Warehouse { Id = Guid.NewGuid(), Name = "Receiving participation" };
        var sku = new StockKeepingUnit { Id = Guid.NewGuid(), Name = "Participation SKU" };
        var zone = Zone(warehouse.Id, "RP", ZoneType.Receiving);
        var firstLocation = Location(warehouse.Id, zone, 1);
        var secondLocation = Location(warehouse.Id, zone, 2);
        var snapshot = new ReceivingOrderImportSnapshot(
            Guid.NewGuid(), false, true, "Participation", DateTime.UtcNow, warehouse.Id, null,
            ReceivingOrderStatus.ReadyForReceiving, default, WarehouseOperation.VendorReceipt,
            BusinessOperation.VendorPurchase, Guid.NewGuid(), default, Guid.NewGuid(), null,
            [new(1, sku.Id, 5m, 5m, null)]);

        await using (var seed = database.CreateDbContext())
        {
            seed.AddRange(warehouse, sku, zone, firstLocation, secondLocation, Value(ReceivingOrder.Create(snapshot, DateTimeOffset.UtcNow)));
            await seed.SaveChangesAsync();
        }

        var interceptor = new ParticipantSaveBarrier();
        var factory = database.WithInterceptor(interceptor);
        var service = ReceivingService(factory);
        var requests = new[] { Command("participant-a"), Command("participant-b") };
        var results = await Task.WhenAll(
            service.JoinReceivingAsync(new(snapshot.Id, firstLocation.Id), requests[0]),
            service.JoinReceivingAsync(new(snapshot.Id, secondLocation.Id), requests[1]));

        Assert.Single(results, result => result.IsSuccess);
        Error(results.Single(x => !x.IsSuccess), OperationErrorType.Conflict);

        var loser = results[0].IsSuccess ? 1 : 0;
        var retry = await ReceivingService(database).JoinReceivingAsync(
            new(snapshot.Id, loser == 0 ? firstLocation.Id : secondLocation.Id), requests[loser]);
        Success(retry);

        await using var verify = database.CreateDbContext();
        var order = await verify.ReceivingOrders.Include(x => x.Participants).SingleAsync(x => x.Id == snapshot.Id);
        Assert.Equal(ReceivingOrderStatus.InReceiving, order.Status);
        Assert.True(order.ReceivingLocationId is not null);
        Assert.Equal(2, order.Participants.Count);
        Assert.All(order.Participants, participant => Assert.Equal(order.Id, participant.ReceivingOrderId));
    }

    private static LicensePlateNumberService LpnService(IDbContextFactory<ApplicationDbContext> factory) =>
        new(new CommandExecutor(factory), factory);

    private static ReceivingOrderCommandService ReceivingService(IDbContextFactory<ApplicationDbContext> factory) =>
        new(
            new CommandExecutor(factory),
            new InventoryPostingService(NullLogger<InventoryPostingService>.Instance),
            new ReceivingOrderSynchronizationService(factory, new RejectingSource(), NullLogger<ReceivingOrderSynchronizationService>.Instance),
            new RejectingSink(),
            NullLogger<ReceivingOrderCommandService>.Instance);

    private sealed class ParticipantSaveBarrier : SaveChangesInterceptor
    {
        private int _arrivals;
        private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<ReceivingOrderParticipant>().Any(x => x.State == EntityState.Added))
            {
                if (Interlocked.Increment(ref _arrivals) == 2)
                    _ready.TrySetResult();
                await _ready.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            }
            return result;
        }
    }

    private sealed class RejectingSource : IReceivingOrderSource
    {
        public Task<OperationResult<ReceivingOrderImportSnapshot>> GetSnapshotAsync(Guid orderId, CancellationToken ct = default) =>
            throw new InvalidOperationException("Joining receiving must not call 1C.");
    }

    private sealed class RejectingSink : IReceivingOrderExecutionSink
    {
        public Task<OperationResult> SetInReceivingAsync(Guid orderId, CancellationToken ct) => Reject();
        public Task<OperationResult> UpdateItemsAsync(Guid orderId, IReadOnlyCollection<ReceivingOrderItem> items, CancellationToken ct) => Reject();
        public Task<OperationResult> SetReceivedAsync(Guid orderId, CancellationToken ct) => Reject();
        private static Task<OperationResult> Reject() => throw new InvalidOperationException("Joining receiving must not call 1C.");
    }
}
