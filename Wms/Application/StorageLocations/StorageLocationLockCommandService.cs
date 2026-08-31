using Microsoft.EntityFrameworkCore;
using Wms.Application.Persistence;
using Wms.Common;
using Wms.Data;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.StorageLocations;

public sealed class StorageLocationLockCommandService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory)
{
    public async Task<OperationResult> LockManuallyAsync(
        Guid storageLocationId,
        string? reason,
        string userId,
        CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        var location = await dbContext.StorageLocations
            .Include(x => x.ActiveLock)
            .Include(x => x.Warehouse)
            .Include(x => x.Zone)
            .SingleOrDefaultAsync(x => x.Id == storageLocationId, ct);

        if (location is null)
        {
            return OperationError.NotFound($"Складская позиция '{storageLocationId}' не найдена.");
        }

        if (location.DeletionMark
            || location.IsFolder
            || location.Warehouse is null
            || location.Warehouse.DeletionMark
            || location.Zone is null
            || location.Zone.DeletionMark)
        {
            return OperationError.Invalid(
                "Заблокировать можно только активную операционную складскую позицию.");
        }

        if (location.ActiveLock is not null)
        {
            return StorageLocationAvailability.LockedConflict(location);
        }

        var lockResult = StorageLocationLock.CreateManual(
            location.Id,
            reason,
            DateTimeOffset.UtcNow,
            userId);
        if (!lockResult.IsSuccess)
        {
            return lockResult.Error!;
        }

        location.AdvanceOperationalRevision();
        dbContext.StorageLocationLocks.Add(lockResult.Value!);
        return await SaveChangesAsync(dbContext, ct);
    }

    public async Task<OperationResult> UnlockManualAsync(
        Guid storageLocationId,
        string userId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return OperationError.Invalid("Не удалось определить текущего пользователя.");
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        var location = await dbContext.StorageLocations
            .Include(x => x.ActiveLock)
            .Include(x => x.Zone)
            .SingleOrDefaultAsync(x => x.Id == storageLocationId, ct);

        if (location is null)
        {
            return OperationError.NotFound($"Складская позиция '{storageLocationId}' не найдена.");
        }

        if (location.ActiveLock is null)
        {
            return OperationError.Conflict(
                $"Ячейка {StorageLocationAvailability.GetAddress(location)} не заблокирована.");
        }

        if (location.ActiveLock.OwnerType != StorageLocationLockOwnerType.Manual)
        {
            return OperationError.Conflict(
                $"Ячейка {GetAddress(location)} заблокирована документом и освобождается только его завершением или отменой.");
        }

        location.AdvanceOperationalRevision();
        dbContext.StorageLocationLocks.Remove(location.ActiveLock);
        return await SaveChangesAsync(dbContext, ct);
    }

    private static async Task<OperationResult> SaveChangesAsync(
        ApplicationDbContext dbContext,
        CancellationToken ct)
    {
        try
        {
            await dbContext.SaveChangesAsync(ct);
            return OperationResult.Success();
        }
        catch (DbUpdateException exception)
            when (PersistenceConflictClassifier.TryClassify(exception, out var error))
        {
            return error;
        }
    }

    private static string GetAddress(StorageLocation location) =>
        StorageLocationAvailability.GetAddress(location);
}
