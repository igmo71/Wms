using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Wms.Application.Commands;
using Wms.Common;
using Wms.Data;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Tests.Infrastructure;

internal static class TestSupport
{
    public static CommandContext Command(string user = "test-user") => new(Guid.NewGuid(), user);

    public static T Value<T>(OperationResult<T> result)
    {
        Assert.True(result.IsSuccess, result.Error?.Message);
        return result.Value!;
    }

    public static void Success(OperationResult result) => Assert.True(result.IsSuccess, result.Error?.Message);

    public static void Error(OperationResult result, OperationErrorType type) => Assert.Equal(type, result.Error?.Type);

    public static Zone Zone(Guid warehouseId, string code, ZoneType type) =>
        Value(Wms.Domain.Zone.Create(Guid.NewGuid(), warehouseId, code, code, type));

    public static StorageLocation Location(Guid warehouseId, Zone zone, int number) =>
        Value(StorageLocation.Create(
            Guid.NewGuid(), warehouseId, zone.Id, null, number, number.ToString(),
            Value(StorageLocationDetails.Create("Test location", false, LocationDimensions.Empty, LocationCoordinates.Empty, null))));

    public static InventoryBalance Balance(Guid warehouseId, Guid locationId, Guid skuId, decimal quantity) =>
        Value(InventoryBalance.Create(Guid.NewGuid(), warehouseId, locationId, skuId, quantity, DateTimeOffset.UtcNow));
}

internal sealed class ReceiptSaveBarrier : SaveChangesInterceptor
{
    private int _arrivals;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context!.ChangeTracker.Entries<CommandReceipt>().Any(x => x.State == EntityState.Added))
        {
            if (Interlocked.Increment(ref _arrivals) == 2)
                _ready.TrySetResult();
            await _ready.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
        }
        return result;
    }
}
