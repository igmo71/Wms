using System.Globalization;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Wms.Application.Authorization;
using Wms.Application.CommandExecution;
using Wms.Application.Inventory;
using Wms.Application.OpeningImports;
using Wms.Common;
using Wms.Data;
using Wms.Domain;
using Wms.Domain.Enums;
using Wms.Tests.Infrastructure;
using static Wms.Tests.Infrastructure.TestSupport;

namespace Wms.Tests.Inventory;

[Collection(SqlDatabaseCollection.Name)]
public sealed class OpeningStockImportTests(SqlDatabase database)
{
    private const string Actor = "opening-admin";
    private static readonly byte[] Workbook = [1, 3, 5, 7];
    private static readonly DateTimeOffset SnapshotAt =
        new(2026, 11, 2, 12, 0, 0, TimeSpan.FromHours(3));
    private static readonly string[] ReviewedAddresses =
    [
        "1H05A7", "1K03A5", "1O08C6", "1R04A5",
        "1R05B1", "1T03C3", "2A02F2", "3E07B1"
    ];

    [Fact]
    public async Task Signed_dated_duplicates_and_eight_reviewed_mixed_cells_post_once_without_inflow_bypass()
    {
        var warehouse = new Warehouse { Id = Guid.NewGuid(), Name = "Opening stock" };
        var firstSku = new StockKeepingUnit { Id = Guid.NewGuid(), Name = "First" };
        var secondSku = new StockKeepingUnit { Id = Guid.NewGuid(), Name = "Second" };
        var date = new DateOnly(2027, 1, 15);
        var rows = new List<OpeningExportRow>();
        foreach (var address in ReviewedAddresses)
        {
            rows.Add(Row(rows.Count + 2, address, "Комплектация", firstSku.Id, 1m));
            rows.Add(Row(rows.Count + 2, address, "Комплектация", secondSku.Id, 1m));
        }
        rows.Add(Row(rows.Count + 2, "1A01A1", "Комплектация", firstSku.Id, 5m, date));
        rows.Add(Row(rows.Count + 2, "1A01A1", "Комплектация", firstSku.Id, -2m, date));
        rows.Add(Row(rows.Count + 2, "1A01A1", "Комплектация", firstSku.Id, 4m,
            requiresDate: true));
        rows.Add(Row(rows.Count + 2, "9Z99A1", "Отгрузка", firstSku.Id, 2m));
        rows.Add(Row(rows.Count + 2, "9Z99A1", "Отгрузка", secondSku.Id, -3m));
        var reader = new FixedReader(rows);
        var cells = await SeedAsync(warehouse, firstSku, secondSku, rows);

        var wrongUnit = new FixedReader([rows[0] with { UnitOfMeasure = "кг" }]);
        var wrongUnitPlan = await new PreviewOpeningStock(wrongUnit, new OpeningImportStore(database))
            .BuildPlanAsync(warehouse.Id, Workbook, SnapshotAt);
        Assert.False(wrongUnitPlan.CanConfirm);
        Assert.Contains(wrongUnitPlan.Issues, x => x.BlocksConfirmation
            && x.Message == "Единица выгрузки не совпадает с базовой единицей SKU.");

        var plan = await new PreviewOpeningStock(reader, new OpeningImportStore(database))
            .BuildPlanAsync(warehouse.Id, Workbook, SnapshotAt);
        Assert.True(plan.CanConfirm, string.Join("; ", plan.Issues
            .Where(x => x.BlocksConfirmation).Select(x => x.Message)));
        Assert.Equal(8, plan.Counts.ReviewedMixedCells);
        Assert.Equal(0, plan.Counts.UnreviewedMixedCells);
        var command = new ConfirmOpeningStockCommand(Guid.NewGuid(), warehouse.Id,
            Workbook, SnapshotAt, plan.Signature, Actor);
        var confirm = Confirm(reader, database);
        var importId = Value(await confirm.ExecuteAsync(command));
        Assert.Equal(importId, Value(await confirm.ExecuteAsync(command)));

        await using (var verify = database.CreateDbContext())
        {
            var movements = await verify.InventoryMovements
                .Where(x => x.WarehouseId == warehouse.Id).ToArrayAsync();
            Assert.Equal(rows.Count, movements.Length);
            var sourceRows = rows.ToDictionary(x => x.RowNumber);
            Assert.All(movements, x =>
            {
                Assert.Equal(RecorderType.OpeningBalance, x.RecorderType);
                Assert.Equal(importId, x.RecorderId);
                Assert.True(x.Quantity > 0);
                Assert.NotNull(x.PostedAtUtc);
                if (sourceRows[x.RecorderLineNumber!.Value].Quantity < 0)
                {
                    Assert.NotNull(x.SourceStorageLocationId);
                    Assert.Null(x.DestinationStorageLocationId);
                }
                else
                {
                    Assert.Null(x.SourceStorageLocationId);
                    Assert.NotNull(x.DestinationStorageLocationId);
                }
            });
            Assert.Equal(rows.Count, movements.Select(x => x.RecorderLineNumber).Distinct().Count());
            Assert.Equal(rows.Count, await verify.InventoryTurnovers
                .CountAsync(x => x.WarehouseId == warehouse.Id));
            var shipping = Assert.Single(movements.Where(x => x.RecorderLineNumber == rows[^1].RowNumber));
            Assert.Equal(cells["9Z99A1"].Id, shipping.SourceStorageLocationId);
            Assert.Null(shipping.DestinationStorageLocationId);
            Assert.Equal(3m, shipping.Quantity);

            var balances = await verify.InventoryBalances
                .Where(x => x.WarehouseId == warehouse.Id).ToArrayAsync();
            Assert.Equal(20, balances.Length);
            Assert.Equal(3m, Assert.Single(balances.Where(x =>
                x.StorageLocationId == cells["1A01A1"].Id
                && x.StockKeepingUnitId == firstSku.Id && x.ExpirationDate == date)).Quantity);
            Assert.Equal(4m, Assert.Single(balances.Where(x =>
                x.StorageLocationId == cells["1A01A1"].Id
                && x.StockKeepingUnitId == firstSku.Id && x.ExpirationDate == null)).Quantity);
            Assert.Equal(-3m, Assert.Single(balances.Where(x =>
                x.StorageLocationId == cells["9Z99A1"].Id
                && x.StockKeepingUnitId == secondSku.Id)).Quantity);
            Assert.Equal(-3m, Assert.Single(await verify.InventoryTurnovers
                .Where(x => x.StorageLocationId == cells["9Z99A1"].Id
                    && x.StockKeepingUnitId == secondSku.Id)
                .ToArrayAsync()).QuantityDelta);
            var datedTurnovers = await verify.InventoryTurnovers
                .Where(x => x.StorageLocationId == cells["1A01A1"].Id
                    && x.ExpirationDate == date)
                .OrderBy(x => x.BalanceBefore).ToArrayAsync();
            Assert.Equal(2, datedTurnovers.Length);
            Assert.Contains(datedTurnovers, x => x.BalanceBefore == 0m
                && x.QuantityDelta == 5m && x.BalanceAfter == 5m);
            Assert.Contains(datedTurnovers, x => x.BalanceBefore == 5m
                && x.QuantityDelta == -2m && x.BalanceAfter == 3m);

            var savedSku = await verify.StockKeepingUnits.SingleAsync(x => x.Id == firstSku.Id);
            Assert.True(savedSku.TracksExpirationDate);
            Assert.False((await verify.StockKeepingUnits.SingleAsync(x => x.Id == secondSku.Id))
                .TracksExpirationDate);
            var import = await verify.WarehouseOpeningImports.SingleAsync(x => x.Id == importId);
            Assert.Equal(WarehouseOpeningImportStatus.StockImported, import.Status);
            Assert.Equal(SnapshotAt, import.DeclaredSnapshotAt);
            Assert.Contains(ReviewedAddresses[0], import.StockSummary);
            Assert.Equal(1, await verify.CommandReceipts.CountAsync(x => x.RequestId == command.RequestId));
        }

        var violations = await new OpeningImportStore(database)
            .LoadViolationsAsync(warehouse.Id, default);
        Assert.Equal(8, violations.Count);
        Assert.All(ReviewedAddresses, address => Assert.Contains(violations,
            x => x.StorageLocationId == cells[address].Id && x.DistinctSkuCount == 2));

        var secondRequest = await confirm.ExecuteAsync(command with
        {
            RequestId = Guid.NewGuid(), UserId = "another-admin"
        });
        Error(secondRequest, OperationErrorType.Conflict);
        var changedWorkbook = await confirm.ExecuteAsync(command with
        {
            RequestId = Guid.NewGuid(), UserId = "another-admin",
            Workbook = new byte[] { 2, 4, 6, 8 }
        });
        Error(changedWorkbook, OperationErrorType.Conflict);
        await using (var verifyReplay = database.CreateDbContext())
            Assert.Equal(rows.Count, await verifyReplay.InventoryMovements
                .CountAsync(x => x.WarehouseId == warehouse.Id));

        var receivingZone = Zone(warehouse.Id, "OPEN-RECEIVING", ZoneType.Receiving);
        var receiving = Location(warehouse.Id, receivingZone, 1);
        var batch = Value(LicensePlateNumberBatch.Issue(1, Actor, DateTimeOffset.UtcNow));
        await using (var seed = database.CreateDbContext())
        {
            seed.AddRange(receivingZone, receiving, batch,
                Balance(warehouse.Id, receiving.Id, secondSku.Id, 2m));
            await seed.SaveChangesAsync();
        }
        var movement = Value(InventoryMovement.Create(Guid.NewGuid(), warehouse.Id,
            receiving.Id, cells[ReviewedAddresses[0]].Id, secondSku.Id, 1m,
            DateTimeOffset.UtcNow, RecorderType.ReceivingOrder, Guid.NewGuid(), 1,
            Actor, licensePlateNumberId: batch.Labels.Single().Id));
        var executor = new CommandExecutor(new CommandExecutionStore(database));
        var posting = new PostInventoryMovement.Handler(new InventoryPostingStore());
        var blocked = await executor.ExecuteAsync("test.opening-mixed-inflow",
            Guid.NewGuid(), CommandExecutor.ComputeHash(movement.Id.ToString()), Actor,
            async (context, ct) =>
            {
                var result = await posting.HandleAsync(new(movement,
                    InventoryPostingKind.Putaway, DateTimeOffset.UtcNow.AddSeconds(1)),
                    context, ct);
                if (!result.IsSuccess)
                    return OperationResult<CommandExecutionResult>.Failure(result.Error!);
                return new CommandExecutionResult(movement.Id, []);
            }, default);
        Error(blocked, OperationErrorType.Conflict);
        await using var final = database.CreateDbContext();
        Assert.False(await final.InventoryMovements.AnyAsync(x => x.Id == movement.Id));
    }

    [Fact]
    public async Task Failed_final_save_leaves_topology_stage_and_no_stock_effects()
    {
        var warehouse = new Warehouse { Id = Guid.NewGuid(), Name = "Atomic opening" };
        var sku = new StockKeepingUnit { Id = Guid.NewGuid(), Name = "Dated" };
        var other = new StockKeepingUnit { Id = Guid.NewGuid(), Name = "Other" };
        var rows = new[]
        {
            Row(2, "1A01A1", "Комплектация", sku.Id, 2m, requiresDate: true),
            Row(3, "1A01A1", "Комплектация", sku.Id, 3m)
        };
        var reader = new FixedReader(rows);
        await SeedAsync(warehouse, sku, other, rows);
        var plan = await new PreviewOpeningStock(reader, new OpeningImportStore(database))
            .BuildPlanAsync(warehouse.Id, Workbook, SnapshotAt);
        Assert.True(plan.CanConfirm);
        var command = new ConfirmOpeningStockCommand(Guid.NewGuid(), warehouse.Id,
            Workbook, SnapshotAt, plan.Signature, Actor);
        var failing = Confirm(reader, database.WithInterceptor(new BreakOpeningStockAuditReference()));
        await Assert.ThrowsAsync<DbUpdateException>(() => failing.ExecuteAsync(command));

        await using var verify = database.CreateDbContext();
        var import = await verify.WarehouseOpeningImports
            .SingleAsync(x => x.WarehouseId == warehouse.Id);
        Assert.Equal(WarehouseOpeningImportStatus.TopologyImported, import.Status);
        Assert.Null(import.DeclaredSnapshotAt);
        Assert.False((await verify.StockKeepingUnits.SingleAsync(x => x.Id == sku.Id))
            .TracksExpirationDate);
        Assert.False(await verify.InventoryBalances.AnyAsync(x => x.WarehouseId == warehouse.Id));
        Assert.False(await verify.InventoryMovements.AnyAsync(x => x.WarehouseId == warehouse.Id));
        Assert.False(await verify.InventoryTurnovers.AnyAsync(x => x.WarehouseId == warehouse.Id));
        Assert.False(await verify.CommandReceipts.AnyAsync(x => x.RequestId == command.RequestId));
    }

    [Fact]
    public async Task Concurrent_administrators_confirm_one_snapshot_and_sql_rejects_duplicate_import_and_line()
    {
        var warehouse = new Warehouse { Id = Guid.NewGuid(), Name = "Concurrent opening" };
        var sku = new StockKeepingUnit { Id = Guid.NewGuid(), Name = "Concurrent SKU" };
        var other = new StockKeepingUnit { Id = Guid.NewGuid(), Name = "Other" };
        var rows = new[] { Row(2, "1A01A1", "Комплектация", sku.Id, 2m) };
        var reader = new FixedReader(rows);
        var cells = await SeedAsync(warehouse, sku, other, rows);
        var plan = await new PreviewOpeningStock(reader, new OpeningImportStore(database))
            .BuildPlanAsync(warehouse.Id, Workbook, SnapshotAt);
        Assert.True(plan.CanConfirm);

        var permission = new ConcurrentAdministrator();
        var firstCommand = new ConfirmOpeningStockCommand(Guid.NewGuid(), warehouse.Id,
            Workbook, SnapshotAt, plan.Signature, "first-admin");
        var secondCommand = firstCommand with
        {
            RequestId = Guid.NewGuid(), UserId = "second-admin"
        };
        var results = await Task.WhenAll(
            Confirm(reader, database, permission).ExecuteAsync(firstCommand),
            Confirm(reader, database, permission).ExecuteAsync(secondCommand));
        var winner = Assert.Single(results.Where(x => x.IsSuccess));
        Error(Assert.Single(results.Where(x => !x.IsSuccess)), OperationErrorType.Conflict);
        var importId = Value(winner);

        await using (var verify = database.CreateDbContext())
        {
            Assert.Equal(1, await verify.WarehouseOpeningImports.CountAsync(x => x.WarehouseId == warehouse.Id));
            Assert.Equal(1, await verify.InventoryMovements.CountAsync(x => x.WarehouseId == warehouse.Id));
            Assert.Equal(1, await verify.InventoryTurnovers.CountAsync(x => x.WarehouseId == warehouse.Id));
            Assert.Equal(2m, (await verify.InventoryBalances.SingleAsync(x => x.WarehouseId == warehouse.Id)).Quantity);
            Assert.Equal(1, await verify.CommandReceipts.CountAsync(x =>
                x.RequestId == firstCommand.RequestId || x.RequestId == secondCommand.RequestId));
        }

        await using (var duplicateImport = database.CreateDbContext())
        {
            duplicateImport.WarehouseOpeningImports.Add(Value(WarehouseOpeningImport.CreateTopology(
                warehouse.Id, "duplicate.xlsx", Convert.ToHexString(SHA256.HashData(Workbook)),
                "{}", Actor, DateTimeOffset.UtcNow)));
            var failure = await Assert.ThrowsAsync<DbUpdateException>(
                () => duplicateImport.SaveChangesAsync());
            Assert.Contains(DatabaseObjectNames.WarehouseOpeningImportsWarehouseIndex,
                failure.InnerException?.Message);
        }

        await using (var duplicateLine = database.CreateDbContext())
        {
            var movement = Value(InventoryMovement.Create(Guid.NewGuid(), warehouse.Id,
                null, cells["1A01A1"].Id, sku.Id, 1m, DateTimeOffset.UtcNow,
                RecorderType.OpeningBalance, importId, rows[0].RowNumber, Actor));
            Success(movement.Post(DateTimeOffset.UtcNow));
            duplicateLine.InventoryMovements.Add(movement);
            var failure = await Assert.ThrowsAsync<DbUpdateException>(
                () => duplicateLine.SaveChangesAsync());
            Assert.Contains(DatabaseObjectNames.InventoryMovementsOpeningBalanceLineIndex,
                failure.InnerException?.Message);
        }
    }

    private async Task<Dictionary<string, StorageLocation>> SeedAsync(
        Warehouse warehouse, StockKeepingUnit firstSku, StockKeepingUnit secondSku,
        IReadOnlyCollection<OpeningExportRow> rows)
    {
        var unit = new UnitOfMeasure
        {
            Id = Guid.NewGuid(), Name = "Штука", Description = "шт"
        };
        firstSku.BaseUnitOfMeasureId = unit.Id;
        secondSku.BaseUnitOfMeasureId = unit.Id;
        var storageZone = Zone(warehouse.Id, "OPEN-STORAGE", ZoneType.Storage);
        var shippingZone = Zone(warehouse.Id, "OPEN-SHIPPING", ZoneType.Shipping);
        var cells = rows.Select(x => x.Address!).Distinct(StringComparer.Ordinal)
            .ToDictionary(x => x, x =>
            {
                var zone = x[0] == '9' ? shippingZone : storageZone;
                return Value(StorageLocation.Create(Guid.NewGuid(), warehouse.Id,
                    zone.Id, null, x, Value(StorageLocationDetails.Create(x, false,
                        LocationDimensions.Empty, LocationCoordinates.Empty, null))));
            }, StringComparer.Ordinal);
        var hash = Convert.ToHexString(SHA256.HashData(Workbook));
        var import = Value(WarehouseOpeningImport.CreateTopology(warehouse.Id,
            "opening.xlsx", hash, "{}", Actor, DateTimeOffset.UtcNow));
        await using var seed = database.CreateDbContext();
        seed.AddRange(warehouse, unit, firstSku, secondSku,
            storageZone, shippingZone, import);
        seed.AddRange(cells.Values);
        await seed.SaveChangesAsync();
        return cells;
    }

    private static OpeningExportRow Row(int number, string address, string kind,
        Guid skuId, decimal quantity, DateOnly? date = null, bool requiresDate = false)
    {
        var (area, group) = address[0] switch
        {
            '1' => ("(1) Фронтальные стеллажи", "Помещение 1"),
            '2' => ("(2) Мезонин 1 этаж 1", "Помещение 2"),
            '3' => ("(3) Мезонин 1 этаж 2", "Помещение 3"),
            _ => ("Ворота", "Ворота")
        };
        return new OpeningExportRow
        {
            RowNumber = number, Area = area, CellGroup = group,
            Address = address, CellKind = kind, SkuName = skuId.ToString(),
            SourceSkuGuid = skuId.ToString(), SkuId = skuId,
            UnitOfMeasure = "шт",
            SourceQuantity = quantity.ToString(CultureInfo.InvariantCulture),
            Quantity = quantity, SourceDate = date?.ToString("dd.MM.yyyy"),
            ExpirationDate = date, RequiresExpirationDate = requiresDate
        };
    }

    private static ConfirmOpeningStock Confirm(IOpeningExportReader reader,
        IDbContextFactory<ApplicationDbContext> factory,
        IWarehouseAdministratorPermissionStore? permission = null) =>
        new(new CommandExecutor(new CommandExecutionStore(factory)), reader,
            new OpeningImportStore(factory), permission ?? new AllowAdministrator(),
            new PostOpeningStock(new InventoryPostingStore()), TimeProvider.System);

    private sealed class FixedReader(IReadOnlyList<OpeningExportRow> rows) : IOpeningExportReader
    {
        public OpeningExportReadResult Read(ReadOnlyMemory<byte> workbook, CancellationToken ct) =>
            new(rows, []);
    }

    private sealed class AllowAdministrator : IWarehouseAdministratorPermissionStore
    {
        public Task<bool> HasAdministratorRoleAsync(string userId, CancellationToken ct) =>
            Task.FromResult(true);
    }

    private sealed class ConcurrentAdministrator : IWarehouseAdministratorPermissionStore
    {
        private int _arrivals;
        private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<bool> HasAdministratorRoleAsync(string userId, CancellationToken ct)
        {
            if (Interlocked.Increment(ref _arrivals) == 2)
                _ready.TrySetResult();
            await _ready.Task.WaitAsync(TimeSpan.FromSeconds(20), ct);
            return true;
        }
    }

    private sealed class BreakOpeningStockAuditReference : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            foreach (var entry in eventData.Context!.ChangeTracker.Entries<WarehouseAuditEvent>()
                .Where(x => x.State == EntityState.Added
                    && x.Entity.EventType == "warehouse.opening.stock.imported"))
                entry.Property(x => x.WarehouseOpeningImportId).CurrentValue = Guid.NewGuid();
            return ValueTask.FromResult(result);
        }
    }
}
