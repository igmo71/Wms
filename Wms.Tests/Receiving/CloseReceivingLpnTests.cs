using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Wms.Application.Authorization;
using Wms.Application.CommandExecution;
using Wms.Application.Inventory;
using Wms.Application.Receiving;
using Wms.Common;
using Wms.Data;
using Wms.Domain;
using Wms.Domain.Enums;
using Wms.Integration.OneS;
using Wms.Tests.Infrastructure;
using static Wms.Tests.Infrastructure.TestSupport;

namespace Wms.Tests.Receiving;

[Collection(SqlDatabaseCollection.Name)]
public sealed class CloseReceivingLpnTests(SqlDatabase database)
{
    [Fact]
    public async Task Closing_posts_once_and_replay_keeps_one_audited_receipt()
    {
        var state = await SeedAsync(5m, 3m);
        var command = new CloseReceivingLpnCommand(
            Guid.NewGuid(), state.OrderId, state.LpnId, "operator-test");
        var close = Handler(state.Reader);
        var first = Value(await close.ExecuteAsync(command));
        Assert.Equal(state.LpnId, first.LpnId);
        Assert.Equal(5m, first.UsedPlan);
        Assert.True(first.SourceCheckSucceeded);
        Assert.Equal(first, Value(await close.ExecuteAsync(command)));

        await using var verify = database.CreateDbContext();
        var lpn = await verify.LicensePlateNumbers.SingleAsync(x => x.Id == state.LpnId);
        Assert.Equal(LicensePlateNumberStatus.Closed, lpn.Status);
        Assert.Equal("operator-test", lpn.ClosedBy);
        var movement = await verify.InventoryMovements.SingleAsync(x =>
            x.LicensePlateNumberId == state.LpnId);
        Assert.Equal(3m, movement.Quantity);
        Assert.Equal(state.LocationId, movement.DestinationStorageLocationId);
        Assert.Equal(3m, (await verify.InventoryBalances.SingleAsync(x =>
            x.StorageLocationId == state.LocationId && x.StockKeepingUnitId == state.SkuId)).Quantity);
        Assert.Equal(3m, (await verify.ReceivingOrderItems.SingleAsync(x =>
            x.ReceivingOrderId == state.OrderId && x.StockKeepingUnitId == state.SkuId)).FactQuantity);
        Assert.Single(await verify.InventoryTurnovers.Where(x =>
            x.InventoryMovementId == movement.Id).ToListAsync());
        var audit = await verify.WarehouseAuditEvents.SingleAsync(x =>
            x.CommandType == "receiving.lpn.close" && x.RequestId == command.RequestId);
        using var values = JsonDocument.Parse(audit.AfterValuesJson!);
        Assert.Equal(5m, values.RootElement.GetProperty("UsedPlan").GetDecimal());
        Assert.True(values.RootElement.GetProperty("SourceCheckSucceeded").GetBoolean());
        Assert.Equal(JsonSerializer.Serialize(new[] { movement.Id }), audit.MovementIdsJson);
        Assert.Single(JsonSerializer.Deserialize<Guid[]>(audit.SourceVersionIdsJson)!);
        Assert.Equal(1, await verify.CommandReceipts.CountAsync(x =>
            x.CommandType == "receiving.lpn.close" && x.RequestId == command.RequestId));
    }

    [Fact]
    public async Task Failed_refresh_uses_last_plan_and_requires_manager_for_excess()
    {
        var state = await SeedAsync(2m, 3m);
        state.Reader.Fail = true;
        var close = Handler(state.Reader);
        var operatorCommand = new CloseReceivingLpnCommand(
            Guid.NewGuid(), state.OrderId, state.LpnId, "operator-test");
        Error(await close.ExecuteAsync(operatorCommand), OperationErrorType.Conflict);
        var managerCommand = new CloseReceivingLpnCommand(
            Guid.NewGuid(), state.OrderId, state.LpnId, "manager-test");
        var result = Value(await close.ExecuteAsync(managerCommand));
        Assert.Equal(state.LpnId, result.LpnId);
        Assert.Equal(2m, result.UsedPlan);
        Assert.False(result.SourceCheckSucceeded);
        Assert.True(result.ManagerException);

        await using var verify = database.CreateDbContext();
        Assert.Equal(1, await verify.InventoryMovements.CountAsync(x =>
            x.LicensePlateNumberId == state.LpnId));
        Assert.False(await verify.CommandReceipts.AnyAsync(x =>
            x.RequestId == operatorCommand.RequestId));
        var audit = await verify.WarehouseAuditEvents.SingleAsync(x =>
            x.RequestId == managerCommand.RequestId && x.CommandType == "receiving.lpn.close");
        using var values = JsonDocument.Parse(audit.AfterValuesJson!);
        Assert.Equal(2m, values.RootElement.GetProperty("UsedPlan").GetDecimal());
        Assert.False(values.RootElement.GetProperty("SourceCheckSucceeded").GetBoolean());
        Assert.True(values.RootElement.GetProperty("ManagerException").GetBoolean());
    }

    [Fact]
    public async Task Current_expiration_flag_blocks_undated_close_and_past_date_needs_confirmation()
    {
        var state = await SeedAsync(5m, 3m);
        await using (var update = database.CreateDbContext())
        {
            var sku = await update.StockKeepingUnits.SingleAsync(x => x.Id == state.SkuId);
            sku.SetExpirationTracking(true);
            await update.SaveChangesAsync();
        }

        var close = Handler(state.Reader);
        var missingDate = new CloseReceivingLpnCommand(
            Guid.NewGuid(), state.OrderId, state.LpnId, "operator-test");
        var missing = await close.ExecuteAsync(missingDate);
        Error(missing, OperationErrorType.Conflict);
        Assert.Contains("укажите дату", missing.Error!.Message, StringComparison.OrdinalIgnoreCase);

        var expiredDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
        var edit = new EditReceivingLpn(
            new CommandExecutor(new CommandExecutionStore(database)),
            new ReceivingWorkStore(database),
            new WarehousePermission(new ReceivingPermission()), TimeProvider.System);
        Value(await edit.ExecuteAsync(new EditReceivingLpnRequest(
            Guid.NewGuid(), state.OrderId, state.LpnId,
            state.SkuId, 3m, expiredDate, "operator-test")));

        var noConfirmation = new CloseReceivingLpnCommand(
            Guid.NewGuid(), state.OrderId, state.LpnId, "operator-test");
        Error(await close.ExecuteAsync(noConfirmation), OperationErrorType.Conflict);
        var confirmed = new CloseReceivingLpnCommand(
            Guid.NewGuid(), state.OrderId, state.LpnId, "operator-test", ConfirmExpiredDate: true);
        Assert.True(Value(await close.ExecuteAsync(confirmed)).ExpiredDateConfirmed);

        await using var verify = database.CreateDbContext();
        Assert.Equal(expiredDate, (await verify.InventoryMovements.SingleAsync(x =>
            x.LicensePlateNumberId == state.LpnId)).ExpirationDate);
        Assert.False(await verify.CommandReceipts.AnyAsync(x =>
            x.RequestId == missingDate.RequestId || x.RequestId == noConfirmation.RequestId));
        var audit = await verify.WarehouseAuditEvents.SingleAsync(x =>
            x.RequestId == confirmed.RequestId && x.CommandType == "receiving.lpn.close");
        using var values = JsonDocument.Parse(audit.AfterValuesJson!);
        Assert.True(values.RootElement.GetProperty("ExpiredDateConfirmed").GetBoolean());
    }

    [Fact]
    public async Task Source_version_change_before_commit_rejects_every_close_effect()
    {
        var state = await SeedAsync(5m, 3m);
        var barrier = new ReceiptBarrier();
        var close = Handler(state.Reader, database.WithInterceptor(barrier));
        var command = new CloseReceivingLpnCommand(
            Guid.NewGuid(), state.OrderId, state.LpnId, "operator-test");
        var pending = close.ExecuteAsync(command);
        await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
        try
        {
            state.Reader.Quantity = 2m;
            var sync = new SynchronizeReceivingSource(state.Reader,
                new ReceivingSourceParser(), new ReceivingSourceStore(database), TimeProvider.System);
            Assert.True((await sync.ExecuteAsync(state.SourceKey)).IsSuccess);
        }
        finally
        {
            barrier.Release.TrySetResult();
        }
        Error(await pending, OperationErrorType.Conflict);

        await using var verify = database.CreateDbContext();
        Assert.Equal(LicensePlateNumberStatus.Open,
            (await verify.LicensePlateNumbers.SingleAsync(x => x.Id == state.LpnId)).Status);
        Assert.False(await verify.InventoryMovements.AnyAsync(x =>
            x.LicensePlateNumberId == state.LpnId));
        Assert.False(await verify.InventoryBalances.AnyAsync(x =>
            x.StorageLocationId == state.LocationId && x.StockKeepingUnitId == state.SkuId));
        Assert.False(await verify.InventoryTurnovers.AnyAsync(x =>
            x.WarehouseId == state.WarehouseId && x.StockKeepingUnitId == state.SkuId));
        Assert.False(await verify.WarehouseAuditEvents.AnyAsync(x =>
            x.CommandType == "receiving.lpn.close" && x.RequestId == command.RequestId));
        Assert.False(await verify.CommandReceipts.AnyAsync(x => x.RequestId == command.RequestId));
    }

    private CloseReceivingLpn Handler(
        MutableReader reader, IDbContextFactory<ApplicationDbContext>? factory = null)
    {
        factory ??= database;
        return new CloseReceivingLpn(
            new CommandExecutor(new CommandExecutionStore(factory)),
            new ReceivingWorkStore(factory),
            new SynchronizeReceivingSource(reader, new ReceivingSourceParser(),
                new ReceivingSourceStore(database), TimeProvider.System),
            new ReceivingSourceParser(),
            new PostInventoryMovement.Handler(new InventoryPostingStore()),
            new WarehousePermission(new ReceivingPermission()), TimeProvider.System);
    }

    private async Task<CloseState> SeedAsync(decimal plan, decimal quantity)
    {
        var now = DateTimeOffset.UtcNow;
        var warehouse = new Warehouse { Id = Guid.NewGuid(), Name = "Close receiving" };
        var sku = new StockKeepingUnit { Id = Guid.NewGuid(), Name = "Close SKU" };
        var zone = Zone(warehouse.Id, "RC", ZoneType.Receiving);
        var location = Location(warehouse.Id, zone, 1);
        var batch = Value(LicensePlateNumberBatch.Issue(1, "operator-test", now));
        var sourceKey = Guid.NewGuid();
        var reader = new MutableReader(sourceKey, warehouse.Id, sku.Id, plan);
        await using (var seed = database.CreateDbContext())
        {
            seed.AddRange(warehouse, sku, zone, location, batch);
            await seed.SaveChangesAsync();
        }
        var parser = new ReceivingSourceParser();
        var sync = new SynchronizeReceivingSource(reader, parser,
            new ReceivingSourceStore(database), TimeProvider.System);
        Assert.True((await sync.ExecuteAsync(sourceKey)).IsSuccess);
        var orderId = Value(await new CreateReceivingOrder(
            new ReceivingOrderStore(database), parser, TimeProvider.System)
            .ExecuteAsync([sourceKey]));
        var lpnId = batch.Labels.Single().Id;
        var joinedAt = DateTimeOffset.UtcNow;
        await using (var prepare = database.CreateDbContext())
        {
            var order = await prepare.ReceivingOrders
                .Include(x => x.Cycles).Include(x => x.Participants)
                .Include(x => x.Items).Include(x => x.LicensePlateNumbers)
                .ThenInclude(x => x.Items).SingleAsync(x => x.Id == orderId);
            var lpn = await prepare.LicensePlateNumbers.Include(x => x.Items)
                .SingleAsync(x => x.Id == lpnId);
            Value(order.JoinCurrentCycle(location.Id, "operator-test", joinedAt));
            prepare.ReceivingOrderParticipants.Add(Assert.Single(order.Participants));
            Assert.True(Value(order.OpenReceivingLpn(lpn, "operator-test", joinedAt)));
            Success(order.EditReceivingLpn(
                lpn, sku.Id, null, quantity, "operator-test", joinedAt, false));
            prepare.LicensePlateNumberItems.Add(Assert.Single(lpn.Items));
            await prepare.SaveChangesAsync();
        }
        return new CloseState(warehouse.Id, orderId, location.Id, sku.Id,
            lpnId, sourceKey, reader);
    }

    private sealed record CloseState(Guid WarehouseId, Guid OrderId, Guid LocationId,
        Guid SkuId, Guid LpnId, Guid SourceKey, MutableReader Reader);

    private sealed class MutableReader(Guid key, Guid warehouseId, Guid skuId, decimal quantity)
        : IReceivingSourceReader
    {
        public bool Fail { get; set; }
        public decimal Quantity { get; set; } = quantity;

        public Task<OperationResult<ReceivingSourceRead>> ReadAsync(
            Guid refKey, CancellationToken ct) =>
            Task.FromResult<OperationResult<ReceivingSourceRead>>(
                Fail ? OperationError.Failure("1С недоступна.")
                    : new ReceivingSourceRead(refKey, $$"""
                        {"value":[{"Ref_Key":"{{key}}","Склад_Key":"{{warehouseId}}",
                        "Posted":true,"DeletionMark":false,"Number":"Close",
                        "Товары":[{"LineNumber":"1","Номенклатура_Key":"{{skuId}}",
                        "Количество":{{Quantity.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}]}]}
                        """));
    }

    private sealed class ReceivingPermission : IWarehousePermissionStore
    {
        public Task<bool> HasManagerRoleAsync(string userId, CancellationToken ct) =>
            Task.FromResult(userId == "manager-test");

        public Task<bool> HasReceivingRoleAsync(string userId, CancellationToken ct) =>
            Task.FromResult(userId is "operator-test" or "manager-test");
    }

    private sealed class ReceiptBarrier : SaveChangesInterceptor
    {
        public TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<CommandReceipt>()
                .Any(x => x.State == EntityState.Added))
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            }
            return result;
        }
    }
}
