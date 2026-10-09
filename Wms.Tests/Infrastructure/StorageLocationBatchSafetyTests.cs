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
}
