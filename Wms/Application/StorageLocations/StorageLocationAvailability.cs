using Wms.Common;
using Wms.Domain;

namespace Wms.Application.StorageLocations;

internal static class StorageLocationAvailability
{
    public static OperationResult ValidateUnlocked(StorageLocation location) =>
        location.ActiveLock is null
            ? OperationResult.Success()
            : LockedConflict(location);

    public static OperationError LockedConflict(StorageLocation location) =>
        OperationError.Conflict(
            $"Ячейка {GetAddress(location)} заблокирована: {location.ActiveLock!.Reason}");

    public static string GetAddress(StorageLocation location) =>
        location.Zone is null ? location.Code : $"{location.Zone.Code}-{location.Code}";
}
