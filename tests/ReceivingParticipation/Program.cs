using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Wms.Application.Commands;
using Wms.Application.Inventory.Movements;
using Wms.Application.ReceivingOrders;
using Wms.Common;
using Wms.Data;
using Wms.Domain;
using Wms.Domain.Enums;

var database = "WmsReceivingParticipation_" + Guid.NewGuid().ToString("N");
var race = new SaveRaceInterceptor();
using var services = new ServiceCollection()
    .Configure<IdentityOptions>(options => options.Stores.SchemaVersion = IdentitySchemaVersions.Version3)
    .BuildServiceProvider();
var options = new DbContextOptionsBuilder<ApplicationDbContext>()
    .UseApplicationServiceProvider(services)
    .UseSqlServer($"Server=(localdb)\\MSSQLLocalDB;Database={database};Integrated Security=true;TrustServerCertificate=true")
    .AddInterceptors(race)
    .Options;
var factory = new ContextFactory(options);
await using var setup = factory.CreateDbContext();

try
{
    Check(!setup.Database.HasPendingModelChanges(), "No migration drift");
    await setup.Database.MigrateAsync();

    var warehouse = new Warehouse { Id = Guid.NewGuid(), Name = "I02" };
    var sku = new StockKeepingUnit { Id = Guid.NewGuid(), Name = "I02 SKU" };
    var zone = Value(Zone.Create(Guid.NewGuid(), warehouse.Id, "R", "Receiving", ZoneType.Receiving));
    var details = Value(StorageLocationDetails.Create("Receiving", false, LocationDimensions.Empty, LocationCoordinates.Empty, null));
    var firstLocation = Value(StorageLocation.Create(Guid.NewGuid(), warehouse.Id, zone.Id, null, 1, "001", details));
    var secondLocation = Value(StorageLocation.Create(Guid.NewGuid(), warehouse.Id, zone.Id, null, 2, "002", details));
    setup.AddRange(warehouse, sku, zone, firstLocation, secondLocation);
    await setup.SaveChangesAsync();

    var sink = new RejectingSink();
    var source = new RejectingSource();
    var service = new ReceivingOrderCommandService(
        new CommandExecutor(factory),
        new InventoryPostingService(NullLogger<InventoryPostingService>.Instance),
        new ReceivingOrderSynchronizationService(factory, source, NullLogger<ReceivingOrderSynchronizationService>.Instance),
        sink,
        NullLogger<ReceivingOrderCommandService>.Instance);

    var orderId = await AddOrderAsync();
    var firstRequest = new CommandContext(Guid.NewGuid(), "operator-a");
    Require(await service.JoinReceivingAsync(new(orderId, firstLocation.Id), firstRequest));
    Require(await service.JoinReceivingAsync(new(orderId, firstLocation.Id), firstRequest));
    Require(await service.JoinReceivingAsync(new(orderId, null), new(Guid.NewGuid(), "operator-b")));
    await using (var verify = factory.CreateDbContext())
    {
        var order = await verify.ReceivingOrders.Include(x => x.Participants).SingleAsync(x => x.Id == orderId);
        Check(order.Status == ReceivingOrderStatus.InReceiving, "Order entered local receiving lifecycle");
        Check(order.ReceivingLocationId == firstLocation.Id, "First participant selected the shared location");
        Check(order.Participants.Count == 2, "Both operators joined once");
        Check(await verify.CommandReceipts.CountAsync(x => x.ResultResourceId == orderId) == 2, "Replay did not add a receipt or participant");
    }
    Check(source.Calls == 0 && sink.Calls == 0, "Joining did not read from or write to 1C");
    Console.WriteLine("PASS: first join, second participant, replay and no outgoing 1C call.");

    var racedOrderId = await AddOrderAsync();
    var requests = new[]
    {
        new CommandContext(Guid.NewGuid(), "operator-a"),
        new CommandContext(Guid.NewGuid(), "operator-b")
    };
    race.Enable();
    var results = await Task.WhenAll(
        service.JoinReceivingAsync(new(racedOrderId, firstLocation.Id), requests[0]),
        service.JoinReceivingAsync(new(racedOrderId, secondLocation.Id), requests[1]));
    race.Disable();
    Check(results.Count(x => x.IsSuccess) == 1, "Only one first join won the location race");
    Expect(results.Single(x => !x.IsSuccess), OperationErrorType.Conflict);
    var loser = results[0].IsSuccess ? 1 : 0;
    Require(await service.JoinReceivingAsync(
        new(racedOrderId, loser == 0 ? firstLocation.Id : secondLocation.Id),
        requests[loser]));
    await using (var verify = factory.CreateDbContext())
    {
        var order = await verify.ReceivingOrders.Include(x => x.Participants).SingleAsync(x => x.Id == racedOrderId);
        Check(order.Participants.Count == 2, "The losing first entrant joined on retry");
        Check(order.ReceivingLocationId is not null, "Exactly one shared receiving location remains assigned");
    }
    Console.WriteLine("PASS: concurrent first entry chooses one location; loser safely joins on retry.");

    var duplicateSnapshot = Snapshot(Guid.NewGuid()) with
    {
        Items =
        [
            new(1, sku.Id, 1m, 1m, null),
            new(2, sku.Id, 2m, 2m, null)
        ]
    };
    setup.Add(Value(ReceivingOrder.Create(duplicateSnapshot, DateTimeOffset.UtcNow)));
    await setup.SaveChangesAsync();
    var duplicateResult = await service.JoinReceivingAsync(
        new(duplicateSnapshot.Id, firstLocation.Id),
        new(Guid.NewGuid(), "operator-a"));
    Expect(duplicateResult, OperationErrorType.Conflict);
    Check(duplicateResult.Error!.Message.Contains("SKU указан в нескольких строках", StringComparison.Ordinal), "Duplicate SKU is explicit");
    Console.WriteLine("PASS: duplicate SKU blocks participation with the required explanation.");

    async Task<Guid> AddOrderAsync()
    {
        var snapshot = Snapshot(Guid.NewGuid());
        setup.Add(Value(ReceivingOrder.Create(snapshot, DateTimeOffset.UtcNow)));
        await setup.SaveChangesAsync();
        return snapshot.Id;
    }

    ReceivingOrderImportSnapshot Snapshot(Guid id) => new(
        id, false, true, $"I02-{id:N}"[..32], DateTime.UtcNow, warehouse.Id, null,
        ReceivingOrderStatus.ReadyForReceiving, default, WarehouseOperation.VendorReceipt,
        BusinessOperation.VendorPurchase, Guid.NewGuid(), default, Guid.NewGuid(), null,
        [new(1, sku.Id, 5m, 5m, null)]);
}
finally
{
    await setup.Database.EnsureDeletedAsync();
}

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void Require(OperationResult result) => Check(result.IsSuccess, result.Error?.Message ?? "Expected success");
static T Value<T>(OperationResult<T> result) { Require(result); return result.Value!; }
static void Expect(OperationResult result, OperationErrorType type) =>
    Check(result.Error?.Type == type, $"Expected {type}, got {result.Error}");

sealed class ContextFactory(DbContextOptions<ApplicationDbContext> options) : IDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext() => new(options);
}

sealed class SaveRaceInterceptor : SaveChangesInterceptor
{
    private volatile bool _enabled;
    private int _arrived;
    private TaskCompletionSource _gate = NewGate();

    public void Enable()
    {
        _arrived = 0;
        _gate = NewGate();
        _enabled = true;
    }

    public void Disable() => _enabled = false;

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (_enabled && eventData.Context!.ChangeTracker.Entries<ReceivingOrderParticipant>()
            .Any(x => x.State == EntityState.Added))
        {
            if (Interlocked.Increment(ref _arrived) == 2)
                _gate.TrySetResult();
            await _gate.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
        }
        return result;
    }

    private static TaskCompletionSource NewGate() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}

sealed class RejectingSource : IReceivingOrderSource
{
    public int Calls { get; private set; }
    public Task<OperationResult<ReceivingOrderImportSnapshot>> GetSnapshotAsync(Guid orderId, CancellationToken ct = default)
    {
        Calls++;
        throw new InvalidOperationException("Join accessed 1C source");
    }
}

sealed class RejectingSink : IReceivingOrderExecutionSink
{
    public int Calls { get; private set; }
    private Task<OperationResult> Reject() { Calls++; throw new InvalidOperationException("Join accessed 1C sink"); }
    public Task<OperationResult> SetInReceivingAsync(Guid orderId, CancellationToken ct) => Reject();
    public Task<OperationResult> UpdateItemsAsync(Guid orderId, IReadOnlyCollection<ReceivingOrderItem> items, CancellationToken ct) => Reject();
    public Task<OperationResult> SetReceivedAsync(Guid orderId, CancellationToken ct) => Reject();
}
