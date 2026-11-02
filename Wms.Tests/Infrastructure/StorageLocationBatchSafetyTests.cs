using Microsoft.EntityFrameworkCore;
using Wms.Application.StorageLocations;
using Wms.Domain;
using Wms.Domain.Enums;
using static Wms.Tests.Infrastructure.TestSupport;

namespace Wms.Tests.Infrastructure;

[Collection(SqlDatabaseCollection.Name)]
public sealed class StorageLocationBatchSafetyTests(SqlDatabase database)
{
    [Fact]
    public async Task Batch_creates_all_addresses_once_and_conflict_creates_none()
    {
        var warehouse = new Warehouse { Id = Guid.NewGuid(), Name = "Batch test" };
        var zone = Zone(warehouse.Id, "ST", ZoneType.Storage);
        var folder = Value(StorageLocation.Create(Guid.NewGuid(), warehouse.Id, null, null,
            "FLEVEL1A01L1", Value(StorageLocationDetails.Create("Уровень 1", true,
                LocationDimensions.Empty, LocationCoordinates.Empty, null))));
        await using (var seed = database.CreateDbContext())
        {
            seed.Warehouses.Add(warehouse);
            seed.Zones.Add(zone);
            seed.StorageLocations.Add(folder);
            await seed.SaveChangesAsync();
        }

        var service = new StorageLocationBatchService(database);
        var firstRange = new StorageLocationRange(warehouse.Id, zone.Id, folder.Id,
            1, 1, "A", "A", 1, 1, "A", "C", 1, 1);
        var firstPreview = Value(await service.PreviewAsync(firstRange));
        Assert.Equal<string>(["1A01A1", "1A01B1", "1A01C1"], firstPreview.Codes);
        Assert.True(firstPreview.CanCreate);
        Assert.Equal(3, Value(await service.CreateAsync(firstRange, firstPreview.Signature)));

        Assert.False((await service.PreviewAsync(firstRange with
        {
            LevelFrom = 2,
            LevelTo = 2
        })).IsSuccess);

        var overlapping = firstRange with { CellFrom = "C", CellTo = "D" };
        var secondPreview = Value(await service.PreviewAsync(overlapping));
        Assert.Contains("1A01C1", secondPreview.Conflicts);
        Assert.False((await service.CreateAsync(overlapping, secondPreview.Signature)).IsSuccess);

        await using var verify = database.CreateDbContext();
        Assert.Equal(3, await verify.StorageLocations.CountAsync(x => x.ParentId == folder.Id));
        Assert.False(await verify.StorageLocations.AnyAsync(x => x.WarehouseId == warehouse.Id && x.Code == "1A01D1"));
    }

    [Fact]
    public async Task Gate_descendants_reject_rack_ranges_and_imported_ancestors_keep_their_room()
    {
        var warehouse = new Warehouse { Id = Guid.NewGuid(), Name = "Range path" };
        var zone = Zone(warehouse.Id, "ST", ZoneType.Storage);
        StorageLocation Folder(string code, Guid? parentId) => Value(StorageLocation.Create(
            Guid.NewGuid(), warehouse.Id, null, parentId, code,
            Value(StorageLocationDetails.Create(code, true,
                LocationDimensions.Empty, LocationCoordinates.Empty, null))));
        var gates = Folder("FGATES", null);
        var gateChild = Folder("GATECART", gates.Id);
        var room = Folder("FROOM1", null);
        var roomChild = Folder("CUSTOM", room.Id);
        await using (var seed = database.CreateDbContext())
        {
            seed.AddRange(warehouse, zone, gates, gateChild, room, roomChild);
            await seed.SaveChangesAsync();
        }

        var service = new StorageLocationBatchService(database);
        var range = new StorageLocationRange(warehouse.Id, zone.Id, gateChild.Id,
            1, 1, "A", "A", 1, 1, "A", "A", 1, 1);
        Assert.False((await service.PreviewAsync(range)).IsSuccess);
        Assert.False((await service.CreateAsync(range, new string('A', 64))).IsSuccess);
        Assert.False((await service.PreviewAsync(range with
        {
            ParentId = roomChild.Id, RoomFrom = 2, RoomTo = 2
        })).IsSuccess);

        await using var verify = database.CreateDbContext();
        Assert.Equal(4, await verify.StorageLocations.CountAsync(x => x.WarehouseId == warehouse.Id));
    }
}
