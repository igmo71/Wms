using Microsoft.EntityFrameworkCore;
using Wms.Application.StorageLocations;
using Wms.Domain;
using Wms.Domain.Enums;
using static Wms.Tests.Infrastructure.TestSupport;

namespace Wms.Tests.Infrastructure;

[Collection(SqlDatabaseCollection.Name)]
public sealed class StorageMapImportSafetyTests(SqlDatabase database)
{
    [Fact]
    public async Task Real_tier_map_import_is_idempotent_and_rejects_conflicting_zone()
    {
        var warehouse = new Warehouse { Id = Guid.NewGuid(), Name = "Map import test" };
        var firstZone = Zone(warehouse.Id, "ST", ZoneType.Storage);
        var secondZone = Zone(warehouse.Id, "ST2", ZoneType.Storage);
        await using (var seed = database.CreateDbContext())
        {
            seed.Warehouses.Add(warehouse);
            seed.Zones.AddRange(firstZone, secondZone);
            await seed.SaveChangesAsync();
        }

        var name = "(1) Фронтальные стеллажи - Ярус - 1.xls";
        var content = await File.ReadAllBytesAsync(Path.Combine(
            AppContext.BaseDirectory, "StorageMaps", name));
        var identity = Value(StorageMapImportService.Identify(name));
        var file = new StorageMapImportFile(name, content, identity, firstZone.Id, true);
        var service = new StorageMapImportService(database);

        var preview = Value(await service.PreviewAsync(warehouse.Id, [file]));
        Assert.False(preview.HasBlockingIssues);
        Assert.True(preview.NewLocationCount > 100);
        Assert.Equal(preview.NewLocationCount, preview.Files.Single().UniqueAddresses);
        Assert.True(preview.NewFolderCount > 0);

        var imported = Value(await service.ImportAsync(warehouse.Id, [file],
            preview.Signature, acknowledgeWarnings: true));
        Assert.Equal(preview.NewLocationCount, imported.CreatedLocations);
        Assert.Equal(preview.NewFolderCount, imported.CreatedFolders);

        var repeatPreview = Value(await service.PreviewAsync(warehouse.Id, [file]));
        Assert.Equal(0, repeatPreview.NewLocationCount);
        Assert.Equal(preview.NewLocationCount, repeatPreview.ExistingLocationCount);
        var repeat = Value(await service.ImportAsync(warehouse.Id, [file],
            repeatPreview.Signature, acknowledgeWarnings: true));
        Assert.Equal(0, repeat.CreatedLocations);
        Assert.Equal(0, repeat.CreatedFolders);

        var conflicting = file with { ZoneId = secondZone.Id };
        var conflictPreview = Value(await service.PreviewAsync(warehouse.Id, [conflicting]));
        Assert.True(conflictPreview.HasBlockingIssues);
        Assert.False((await service.ImportAsync(warehouse.Id, [conflicting],
            conflictPreview.Signature, acknowledgeWarnings: true)).IsSuccess);

        await using var verify = database.CreateDbContext();
        Assert.Equal(preview.NewLocationCount + preview.NewFolderCount,
            await verify.StorageLocations.CountAsync(x => x.WarehouseId == warehouse.Id));
    }
}
