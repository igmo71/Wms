using Wms.Common;
using Wms.Domain.Enums;

namespace Wms.Domain;

public class StorageLocationLock
{
    public const int MaximumReasonLength = 1000;

    private StorageLocationLock()
    {
    }

    public Guid StorageLocationId { get; private set; }
    public StorageLocation? StorageLocation { get; private set; }
    public StorageLocationLockOwnerType OwnerType { get; private set; }
    public Guid? OwnerId { get; private set; }
    public string Reason { get; private set; } = null!;
    public DateTimeOffset LockedAtUtc { get; private set; }
    public string LockedBy { get; private set; } = null!;

    public static OperationResult<StorageLocationLock> CreateManual(
        Guid storageLocationId,
        string? reason,
        DateTimeOffset lockedAtUtc,
        string? lockedBy) =>
        Create(
            storageLocationId,
            StorageLocationLockOwnerType.Manual,
            null,
            reason,
            lockedAtUtc,
            lockedBy);

    internal static OperationResult<StorageLocationLock> CreateForInventoryCount(
        Guid storageLocationId,
        Guid inventoryCountId,
        string? reason,
        DateTimeOffset lockedAtUtc,
        string? lockedBy) =>
        Create(
            storageLocationId,
            StorageLocationLockOwnerType.InventoryCount,
            inventoryCountId,
            reason,
            lockedAtUtc,
            lockedBy);

    private static OperationResult<StorageLocationLock> Create(
        Guid storageLocationId,
        StorageLocationLockOwnerType ownerType,
        Guid? ownerId,
        string? reason,
        DateTimeOffset lockedAtUtc,
        string? lockedBy)
    {
        if (storageLocationId == Guid.Empty)
        {
            return OperationError.Invalid("Идентификатор складской позиции обязателен.");
        }

        var ownerIsValid = ownerType switch
        {
            StorageLocationLockOwnerType.Manual => ownerId is null,
            StorageLocationLockOwnerType.InventoryCount => ownerId is Guid id && id != Guid.Empty,
            _ => false
        };
        if (!ownerIsValid)
        {
            return OperationError.Invalid("Владелец блокировки складской позиции указан некорректно.");
        }

        var normalizedReason = reason?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedReason))
        {
            return OperationError.Invalid("Причина блокировки обязательна.");
        }

        if (normalizedReason.Length > MaximumReasonLength)
        {
            return OperationError.Invalid(
                $"Причина блокировки не должна превышать {MaximumReasonLength} символов.");
        }

        if (lockedAtUtc == default)
        {
            return OperationError.Invalid("Время блокировки обязательно.");
        }

        if (string.IsNullOrWhiteSpace(lockedBy))
        {
            return OperationError.Invalid("Пользователь, установивший блокировку, обязателен.");
        }

        return new StorageLocationLock
        {
            StorageLocationId = storageLocationId,
            OwnerType = ownerType,
            OwnerId = ownerId,
            Reason = normalizedReason,
            LockedAtUtc = lockedAtUtc,
            LockedBy = lockedBy
        };
    }
}
