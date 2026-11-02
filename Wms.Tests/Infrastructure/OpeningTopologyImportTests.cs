using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Wms.Application.Authorization;
using Wms.Application.CommandExecution;
using Wms.Application.OpeningImports;
using Wms.Common;
using Wms.Data;
using Wms.Domain;
using Wms.Domain.Enums;
using static Wms.Tests.Infrastructure.TestSupport;

namespace Wms.Tests.Infrastructure;

[Collection(SqlDatabaseCollection.Name)]
public sealed class OpeningTopologyImportTests(SqlDatabase database)
{
    [Fact]
    public async Task Audit_failure_rolls_back_topology_and_conflicting_existing_address_blocks_import()
    {
        var warehouse = new Warehouse { Id = Guid.NewGuid(), Name = "Opening topology" };
        await using (var seed = database.CreateDbContext())
        {
            seed.Warehouses.Add(warehouse);
            await seed.SaveChangesAsync();
        }

        var reader = new FixedReader();
        var bytes = new byte[] { 1, 2, 3 };
        var preview = await new PreviewOpeningTopology(reader, new OpeningImportStore(database))
            .ExecuteAsync(warehouse.Id, bytes);
        Assert.True(preview.CanConfirm);
        Assert.Equal(2, preview.Counts.DistinctCells);
        var command = new ConfirmOpeningTopologyCommand(
            Guid.NewGuid(), warehouse.Id, "opening.xlsx", bytes,
            preview.Signature, "test-administrator");

        var denied = await Confirm(reader, database, administrator: false)
            .ExecuteAsync(command);
        Error(denied, OperationErrorType.Conflict);

        var failingFactory = database.WithInterceptor(new BreakOpeningAuditReference());
        var failing = Confirm(reader, failingFactory);
        await Assert.ThrowsAsync<DbUpdateException>(() => failing.ExecuteAsync(command));
        await using (var afterFailure = database.CreateDbContext())
        {
            Assert.False(await afterFailure.WarehouseOpeningImports
                .AnyAsync(x => x.WarehouseId == warehouse.Id));
            Assert.False(await afterFailure.Zones.AnyAsync(x => x.WarehouseId == warehouse.Id));
            Assert.False(await afterFailure.StorageLocations.AnyAsync(x => x.WarehouseId == warehouse.Id));
            Assert.False(await afterFailure.CommandReceipts.AnyAsync(x => x.RequestId == command.RequestId));
            Assert.False(await afterFailure.WarehouseAuditEvents.AnyAsync(x =>
                x.CommandType == "warehouse.opening.topology.import"
                && x.RequestId == command.RequestId));
        }

        var confirmation = Confirm(reader, database);
        var importId = Value(await confirmation.ExecuteAsync(command));
        Assert.Equal(importId, Value(await confirmation.ExecuteAsync(command)));
        await using (var afterSuccess = database.CreateDbContext())
        {
            Assert.Equal(1, await afterSuccess.WarehouseOpeningImports
                .CountAsync(x => x.WarehouseId == warehouse.Id));
            Assert.Equal(2, await afterSuccess.StorageLocations
                .CountAsync(x => x.WarehouseId == warehouse.Id && !x.IsFolder));
            Assert.Equal(1, await afterSuccess.WarehouseAuditEvents
                .CountAsync(x => x.WarehouseOpeningImportId == importId));
            Assert.Equal(1, await afterSuccess.CommandReceipts
                .CountAsync(x => x.RequestId == command.RequestId));
        }

        var conflictingWarehouse = new Warehouse { Id = Guid.NewGuid(), Name = "Conflict" };
        var wrongZone = Zone(conflictingWarehouse.Id, "WRONG", ZoneType.Reserve);
        var occupied = Value(StorageLocation.Create(Guid.NewGuid(), conflictingWarehouse.Id,
            wrongZone.Id, null, "1A01A1", Value(StorageLocationDetails.Create(
                "Wrong parent", false, LocationDimensions.Empty, LocationCoordinates.Empty, null))));
        await using (var seed = database.CreateDbContext())
        {
            seed.AddRange(conflictingWarehouse, wrongZone, occupied);
            await seed.SaveChangesAsync();
        }

        var conflictPreview = await new PreviewOpeningTopology(reader, new OpeningImportStore(database))
            .ExecuteAsync(conflictingWarehouse.Id, bytes);
        Assert.False(conflictPreview.CanConfirm);
        var rejected = await confirmation.ExecuteAsync(command with
        {
            RequestId = Guid.NewGuid(), WarehouseId = conflictingWarehouse.Id,
            PreviewSignature = conflictPreview.Signature
        });
        Error(rejected, OperationErrorType.Invalid);
        await using var verifyConflict = database.CreateDbContext();
        Assert.False(await verifyConflict.WarehouseOpeningImports
            .AnyAsync(x => x.WarehouseId == conflictingWarehouse.Id));
        Assert.Equal(1, await verifyConflict.StorageLocations
            .CountAsync(x => x.WarehouseId == conflictingWarehouse.Id));
    }

    private static ConfirmOpeningTopology Confirm(
        IOpeningExportReader reader, IDbContextFactory<ApplicationDbContext> factory,
        bool administrator = true) =>
        new(new CommandExecutor(new CommandExecutionStore(factory)), reader,
            new OpeningImportStore(factory), new AllowAdministrator(administrator), TimeProvider.System);

    private sealed class AllowAdministrator(bool allowed) : IWarehouseAdministratorPermissionStore
    {
        public Task<bool> HasAdministratorRoleAsync(string userId, CancellationToken ct) =>
            Task.FromResult(allowed);
    }

    private sealed class FixedReader : IOpeningExportReader
    {
        public OpeningExportReadResult Read(ReadOnlyMemory<byte> workbook, CancellationToken ct) =>
            new([
                new OpeningExportRow
                {
                    RowNumber = 2, Area = "(1) Фронтальные стеллажи",
                    CellGroup = "Помещение 1", Address = "1A01A1",
                    CellKind = "Комплектация"
                },
                new OpeningExportRow
                {
                    RowNumber = 3, Area = "(1) Фронтальные стеллажи",
                    CellGroup = "Помещение 1", Address = "1A01B1",
                    CellKind = "Хранение"
                }
            ], []);
    }

    private sealed class BreakOpeningAuditReference : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            foreach (var entry in eventData.Context!.ChangeTracker.Entries<WarehouseAuditEvent>()
                .Where(x => x.State == EntityState.Added
                    && x.Entity.WarehouseOpeningImportId is not null))
                entry.Property(x => x.WarehouseOpeningImportId).CurrentValue = Guid.NewGuid();
            return ValueTask.FromResult(result);
        }
    }
}
