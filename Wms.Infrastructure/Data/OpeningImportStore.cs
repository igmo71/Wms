using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Wms.Application.CommandExecution;
using Wms.Application.OpeningImports;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Data;

public sealed class OpeningImportStore(IDbContextFactory<ApplicationDbContext> contextFactory)
    : IOpeningTopologyStateStore, IOpeningTopologyStore,
        IOpeningStockStateStore, IOpeningStockConfirmationStore,
        IOpeningMixedStockViolationStore, IOpeningImportResultStore
{
    public async Task<OpeningImportResultSnapshot?> LoadResultAsync(
        Guid warehouseId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        return await db.WarehouseOpeningImports.AsNoTracking()
            .Where(x => x.WarehouseId == warehouseId)
            .Select(x => new OpeningImportResultSnapshot(
                x.Id, x.WarehouseId, x.SourceName, x.SourceSha256,
                x.Status, x.TopologyImportedAtUtc, x.TopologyImportedBy,
                x.DeclaredSnapshotAt, x.StockImportedAtUtc, x.StockImportedBy,
                x.TopologySummary, x.StockSummary))
            .SingleOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<OpeningMixedStockViolation>> LoadViolationsAsync(
        Guid warehouseId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        var occupied = await db.InventoryBalances.AsNoTracking()
            .Where(x => x.WarehouseId == warehouseId && x.Quantity != 0
                && x.StorageLocation != null
                && !x.StorageLocation.IsFolder
                && !x.StorageLocation.AllowsMultipleSkus
                && x.StorageLocation.Zone != null
                && (x.StorageLocation.Zone.Type == ZoneType.Storage
                    || x.StorageLocation.Zone.Type == ZoneType.Reserve))
            .Select(x => new
            {
                x.StorageLocationId, x.StockKeepingUnitId,
                Address = x.StorageLocation!.Code
            })
            .Distinct().ToArrayAsync(ct);
        return occupied.GroupBy(x => x.StorageLocationId)
            .Where(x => x.Count() > 1)
            .Select(x => new OpeningMixedStockViolation(
                x.Key, x.First().Address, x.Count()))
            .OrderBy(x => x.Address, StringComparer.Ordinal).ToArray();
    }

    public async Task<OpeningTopologyImportState> LoadAsync(
        Guid warehouseId, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        return await LoadStateAsync(db, warehouseId, ct);
    }

    public async Task<OperationResult<OpeningTopologyPlan>> StageAsync(
        ICommandExecutionContext session,
        Guid warehouseId,
        string sourceSha256,
        OpeningExportReadResult source,
        string expectedSignature,
        CancellationToken ct)
    {
        var commandSession = session is CommandExecutionStore.Session current
            ? current
            : throw new InvalidOperationException("Загрузка топологии требует сессию команды WMS.");
        var db = commandSession.Db;
        if (warehouseId == Guid.Empty || source is null
            || sourceSha256 is not { Length: 64 } || !sourceSha256.All(Uri.IsHexDigit)
            || expectedSignature is not { Length: 64 } || !expectedSignature.All(Uri.IsHexDigit))
            return OperationError.Invalid("Данные подтверждения топологии неполны или некорректны.");

        OpeningTopologyImportState state;
        try
        {
            await commandSession.BeginSerializableAsync(ct);
            state = await LoadStateAsync(db, warehouseId, ct);
        }
        catch (SqlException exception) when (exception.Number == 1205)
        {
            return OperationError.Conflict(
                "Состояние склада изменяется другой командой. Обновите предпросмотр и повторите попытку.");
        }
        if (state.Import is { } existing)
            return !string.Equals(existing.SourceSha256, sourceSha256,
                StringComparison.OrdinalIgnoreCase)
                ? OperationError.Conflict(
                    "Для склада уже загружена топология из другого файла. Новая репетиция требует очистки базы.")
                : OperationError.Conflict(
                    "Топология этого склада уже загружена. Проверьте сохранённый протокол; новая репетиция требует очистки базы.");

        var plan = OpeningTopologyPlanBuilder.Build(warehouseId, sourceSha256, source, state);
        if (!string.Equals(plan.Signature, expectedSignature, StringComparison.OrdinalIgnoreCase))
            return OperationError.Conflict("Файл или состояние склада изменились. Обновите предпросмотр.");
        if (!plan.CanConfirm)
            return OperationError.Invalid("Исправьте блокирующие замечания предпросмотра топологии.");

        var zoneIds = state.Zones.ToDictionary(x => x.Code, x => x.Id, StringComparer.Ordinal);
        var folderIds = state.Locations.Where(x => x.IsFolder)
            .ToDictionary(x => x.Code, x => x.Id, StringComparer.Ordinal);
        var newZones = new List<Zone>();
        var newLocations = new List<StorageLocation>();

        foreach (var zone in plan.Zones)
        {
            if (zoneIds.ContainsKey(zone.Code))
                continue;
            var created = Zone.Create(
                Guid.NewGuid(), warehouseId, zone.Code, zone.Name, zone.Type);
            if (!created.IsSuccess)
                return created.Error!;
            zoneIds.Add(zone.Code, created.Value!.Id);
            newZones.Add(created.Value!);
        }

        foreach (var folder in plan.Folders)
        {
            if (folderIds.ContainsKey(folder.Code))
                continue;
            var details = StorageLocationDetails.Create(
                folder.Name, true, LocationDimensions.Empty, LocationCoordinates.Empty, null);
            if (!details.IsSuccess)
                return details.Error!;
            var created = StorageLocation.Create(
                Guid.NewGuid(), warehouseId, null,
                folder.ParentCode is null ? null : folderIds[folder.ParentCode],
                folder.Code, details.Value!);
            if (!created.IsSuccess)
                return created.Error!;
            folderIds.Add(folder.Code, created.Value!.Id);
            newLocations.Add(created.Value!);
        }

        foreach (var cell in plan.Cells)
        {
            if (cell.Exists)
                continue;
            var details = StorageLocationDetails.Create(
                cell.Name, false, LocationDimensions.Empty, LocationCoordinates.Empty,
                null, cell.AllowsMultipleSkus);
            if (!details.IsSuccess)
                return details.Error!;
            var created = StorageLocation.Create(
                Guid.NewGuid(), warehouseId, zoneIds[cell.ZoneCode],
                folderIds[cell.ParentCode], cell.Address, details.Value!);
            if (!created.IsSuccess)
                return created.Error!;
            newLocations.Add(created.Value!);
        }

        // No EF change is staged until every source row and existing match has passed.
        foreach (var zone in newZones)
            session.Stage(zone);
        foreach (var location in newLocations)
            session.Stage(location);
        return plan;
    }

    public async Task<OpeningStockImportState> LoadAsync(
        Guid warehouseId, IReadOnlyCollection<Guid> skuIds, CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);
        return await LoadStockStateAsync(db, warehouseId, skuIds, ct);
    }

    public async Task<OperationResult<OpeningStockConfirmationState>> LoadAsync(
        ICommandExecutionContext session, Guid warehouseId,
        IReadOnlyCollection<Guid> skuIds, CancellationToken ct)
    {
        var commandSession = session is CommandExecutionStore.Session current
            ? current
            : throw new InvalidOperationException("Загрузка остатков требует сессию команды WMS.");
        var db = commandSession.Db;
        try
        {
            await commandSession.BeginSerializableAsync(ct);
            // Serialize stock confirmations for this warehouse before reading the snapshot.
            var imports = await db.WarehouseOpeningImports
                .FromSqlInterpolated($"SELECT * FROM [WarehouseOpeningImports] WITH (UPDLOCK, HOLDLOCK) WHERE [WarehouseId] = {warehouseId}")
                .ToArrayAsync(ct);
            var import = imports.SingleOrDefault();
            var snapshot = await LoadStockStateAsync(db, warehouseId, skuIds, ct);
            var trackedSkus = new Dictionary<Guid, StockKeepingUnit>();
            foreach (var batch in skuIds.Distinct().Chunk(500))
            {
                var loaded = await db.StockKeepingUnits
                    .Where(x => batch.Contains(x.Id)).ToArrayAsync(ct);
                foreach (var sku in loaded)
                    trackedSkus.Add(sku.Id, sku);
            }
            return new OpeningStockConfirmationState(snapshot, import, trackedSkus);
        }
        catch (SqlException exception) when (exception.Number == 1205)
        {
            return OperationError.Conflict(
                "Состояние склада изменяется другой командой. Обновите предпросмотр и повторите попытку.");
        }
    }

    private static async Task<OpeningStockImportState> LoadStockStateAsync(
        ApplicationDbContext db, Guid warehouseId,
        IReadOnlyCollection<Guid> skuIds, CancellationToken ct)
    {
        var warehouse = await db.Warehouses.AsNoTracking()
            .Where(x => x.Id == warehouseId)
            .Select(x => new { x.DeletionMark }).SingleOrDefaultAsync(ct);
        var import = await db.WarehouseOpeningImports.AsNoTracking()
            .Where(x => x.WarehouseId == warehouseId)
            .Select(x => new OpeningStockExistingImport(
                x.Id, x.SourceSha256, x.Status, x.Revision))
            .SingleOrDefaultAsync(ct);
        var locations = await db.StorageLocations.AsNoTracking()
            .Where(x => x.WarehouseId == warehouseId)
            .Select(x => new OpeningStockLocation(
                x.Id, x.Code, x.Zone == null ? null : (ZoneType?)x.Zone.Type,
                x.DeletionMark, x.IsFolder, x.Zone == null || x.Zone.DeletionMark,
                x.ActiveLock != null, x.AllowsMultipleSkus, x.OperationalRevision))
            .ToArrayAsync(ct);
        var skus = new List<OpeningStockSku>();
        foreach (var batch in skuIds.Distinct().Chunk(500))
        {
            skus.AddRange(await db.StockKeepingUnits.AsNoTracking()
                .Where(x => batch.Contains(x.Id))
                .Select(x => new OpeningStockSku(
                    x.Id, x.Name ?? x.Code ?? string.Empty,
                    x.BaseUnitOfMeasure == null ? null : x.BaseUnitOfMeasure.Name,
                    x.BaseUnitOfMeasure == null ? null : x.BaseUnitOfMeasure.Description,
                    x.BaseUnitOfMeasure == null ? null : x.BaseUnitOfMeasure.Abbreviation,
                    x.DeletionMark,
                    x.BaseUnitOfMeasure == null || x.BaseUnitOfMeasure.DeletionMark,
                    x.TracksExpirationDate))
                .ToArrayAsync(ct));
        }
        var hasStock = await db.InventoryBalances.AsNoTracking()
                .AnyAsync(x => x.WarehouseId == warehouseId, ct)
            || await db.InventoryMovements.AsNoTracking()
                .AnyAsync(x => x.WarehouseId == warehouseId, ct)
            || await db.InventoryTurnovers.AsNoTracking()
                .AnyAsync(x => x.WarehouseId == warehouseId, ct);
        var hasWork = await db.ReceivingOrders.AsNoTracking()
                .AnyAsync(x => x.WarehouseId == warehouseId, ct)
            || await db.ShippingOrders.AsNoTracking()
                .AnyAsync(x => x.WarehouseId == warehouseId, ct)
            || await db.InventoryTransfers.AsNoTracking()
                .AnyAsync(x => x.WarehouseId == warehouseId, ct)
            || await db.InventoryCounts.AsNoTracking()
                .AnyAsync(x => x.WarehouseId == warehouseId, ct);
        return new OpeningStockImportState(
            warehouse is not null, warehouse?.DeletionMark ?? false,
            import, locations, skus, hasStock, hasWork);
    }

    private static async Task<OpeningTopologyImportState> LoadStateAsync(
        ApplicationDbContext db, Guid warehouseId, CancellationToken ct)
    {
        var warehouse = await db.Warehouses.AsNoTracking()
            .Where(x => x.Id == warehouseId)
            .Select(x => new { x.DeletionMark })
            .SingleOrDefaultAsync(ct);
        var import = await db.WarehouseOpeningImports.AsNoTracking()
            .Where(x => x.WarehouseId == warehouseId)
            .Select(x => new OpeningTopologyExistingImport(
                x.Id, x.SourceSha256, x.Status, x.Revision))
            .SingleOrDefaultAsync(ct);
        var zones = await db.Zones.AsNoTracking()
            .Where(x => x.WarehouseId == warehouseId)
            .Select(x => new OpeningTopologyExistingZone(
                x.Id, x.Code, x.Name, x.Type, x.DeletionMark))
            .ToArrayAsync(ct);
        var locations = await db.StorageLocations.AsNoTracking()
            .Where(x => x.WarehouseId == warehouseId)
            .Select(x => new OpeningTopologyExistingLocation(
                x.Id, x.Code, x.Name, x.ParentId, x.ZoneId,
                x.IsFolder, x.AllowsMultipleSkus, x.DeletionMark, x.OperationalRevision))
            .ToArrayAsync(ct);
        return new OpeningTopologyImportState(
            warehouse is not null, warehouse?.DeletionMark ?? false,
            import, zones, locations);
    }
}
