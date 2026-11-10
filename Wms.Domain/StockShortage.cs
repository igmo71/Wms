using Wms.Common;
using Wms.Domain.Enums;

namespace Wms.Domain;

public sealed class StockShortage
{
    public const int MaximumCommentLength = 1000;
    private StockShortage() { }

    public Guid Id { get; private set; }
    public Guid WarehouseId { get; private set; }
    public Guid StorageLocationId { get; private set; }
    public Guid StockKeepingUnitId { get; private set; }
    public DateOnly? ExpirationDate { get; private set; }
    public StockShortageOrigin Origin { get; private set; }
    public Guid OriginId { get; private set; }
    public decimal ExpectedQuantity { get; private set; }
    public decimal FoundQuantity { get; private set; }
    public decimal BlockedQuantity => ResolvedAtUtc is null ? ExpectedQuantity - FoundQuantity : 0;
    public string? Comment { get; private set; }
    public string ReportedBy { get; private set; } = null!;
    public DateTimeOffset ReportedAtUtc { get; private set; }
    public string? ResolvedBy { get; private set; }
    public DateTimeOffset? ResolvedAtUtc { get; private set; }
    public string? ResolutionReason { get; private set; }
    public Guid? CorrectionMovementId { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static OperationResult<StockShortage> Report(
        Guid id, Guid warehouseId, Guid storageLocationId, Guid stockKeepingUnitId,
        DateOnly? expirationDate, StockShortageOrigin origin, Guid originId,
        decimal expectedQuantity, decimal foundQuantity, string? comment,
        string userId, DateTimeOffset reportedAtUtc)
    {
        if (id == Guid.Empty || warehouseId == Guid.Empty || storageLocationId == Guid.Empty
            || stockKeepingUnitId == Guid.Empty || originId == Guid.Empty
            || !Enum.IsDefined(origin))
            return OperationError.Invalid("Источник и группа недостачи должны быть определены.");
        if (!WarehouseQuantity.IsPositive(expectedQuantity)
            || !WarehouseQuantity.IsNonNegative(foundQuantity) || foundQuantity >= expectedQuantity)
            return OperationError.Invalid("Найденное количество должно быть меньше ожидаемого с точностью до 0,001.");
        if (comment?.Trim().Length > MaximumCommentLength)
            return OperationError.Invalid("Пояснение недостачи не должно превышать 1000 символов.");
        if (string.IsNullOrWhiteSpace(userId) || userId.Length > 36 || reportedAtUtc == default)
            return OperationError.Invalid("Для недостачи обязательны пользователь и время.");

        return new StockShortage
        {
            Id = id,
            WarehouseId = warehouseId,
            StorageLocationId = storageLocationId,
            StockKeepingUnitId = stockKeepingUnitId,
            ExpirationDate = expirationDate,
            Origin = origin,
            OriginId = originId,
            ExpectedQuantity = expectedQuantity,
            FoundQuantity = foundQuantity,
            Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(),
            ReportedBy = userId.Trim(),
            ReportedAtUtc = reportedAtUtc
        };
    }

    public OperationResult Resolve(
        string userId, DateTimeOffset resolvedAtUtc, string reason, Guid? correctionMovementId = null)
    {
        if (ResolvedAtUtc is not null)
            return OperationError.Conflict("Недостача уже разобрана.");
        if (string.IsNullOrWhiteSpace(userId) || userId.Length > 36
            || resolvedAtUtc == default || resolvedAtUtc < ReportedAtUtc)
            return OperationError.Invalid("Для решения обязательны пользователь и корректное время.");
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > MaximumCommentLength)
            return OperationError.Invalid("Причина решения обязательна и не должна превышать 1000 символов.");
        if (correctionMovementId == Guid.Empty)
            return OperationError.Invalid("Идентификатор корректировки не может быть пустым.");

        ResolvedBy = userId.Trim();
        ResolvedAtUtc = resolvedAtUtc;
        ResolutionReason = reason.Trim();
        CorrectionMovementId = correctionMovementId;
        return OperationResult.Success();
    }
}
