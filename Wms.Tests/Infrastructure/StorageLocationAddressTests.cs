using Microsoft.EntityFrameworkCore;
using Wms.Application.StorageLocations;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;
using static Wms.Tests.Infrastructure.TestSupport;

namespace Wms.Tests.Infrastructure;

[Collection(SqlDatabaseCollection.Name)]
public sealed class StorageLocationAddressTests(SqlDatabase database)
{
    [Fact]
    public async Task Address_is_unique_across_zones_and_old_code39_value_resolves_in_warehouse()
    {
        var warehouse = new Warehouse { Id = Guid.NewGuid(), Name = "Address test" };
        var otherWarehouse = new Warehouse { Id = Guid.NewGuid(), Name = "Other address test" };
        var storageZone = Zone(warehouse.Id, "ST", ZoneType.Storage);
        var transitZone = Zone(warehouse.Id, "TR", ZoneType.Transit);
        var otherZone = Zone(otherWarehouse.Id, "ST", ZoneType.Storage);
        await using (var seed = database.CreateDbContext())
        {
            seed.Warehouses.AddRange(warehouse, otherWarehouse);
            seed.Zones.AddRange(storageZone, transitZone, otherZone);
            await seed.SaveChangesAsync();
        }

        var details = Value(StorageLocationDetails.Create(
            "Ячейка", false, LocationDimensions.Empty, LocationCoordinates.Empty, null));
        var commandService = new StorageLocationCommandService(database);
        var first = Value(await commandService.CreateAsync(new CreateStorageLocationCommand
        {
            WarehouseId = warehouse.Id,
            ZoneId = storageZone.Id,
            ParentId = null,
            Code = "1-Q-01-E-3",
            Details = details
        }));

        var duplicate = await commandService.CreateAsync(new CreateStorageLocationCommand
        {
            WarehouseId = warehouse.Id,
            ZoneId = transitZone.Id,
            ParentId = null,
            Code = "1Q01E3",
            Details = details
        });
        Error(duplicate, OperationErrorType.Conflict);

        Value(await commandService.CreateAsync(new CreateStorageLocationCommand
        {
            WarehouseId = otherWarehouse.Id,
            ZoneId = otherZone.Id,
            ParentId = null,
            Code = "1Q01E3",
            Details = details
        }));

        var queryService = new StorageLocationQueryService(database);
        Assert.Equal(first.Id, Value(await queryService.ResolveBarcodeAsync(
            "1Q01E3", warehouse.Id, ZoneType.Storage)).Id);
        Error(await queryService.ResolveBarcodeAsync("1Q01E3", null, ZoneType.Storage),
            OperationErrorType.Conflict);

        await using var verify = database.CreateDbContext();
        var persisted = await verify.StorageLocations.SingleAsync(x => x.Id == first.Id);
        Assert.Equal("1Q01E3", persisted.Code);
    }
}
