using Wms.Common;

namespace Wms.Domain;

public sealed class LicensePlateNumberItem
{
    private LicensePlateNumberItem() { }

    internal LicensePlateNumberItem(
        Guid licensePlateNumberId,
        Guid stockKeepingUnitId,
        DateOnly? expirationDate,
        decimal quantity,
        DateTimeOffset createdAtUtc,
        string createdBy)
    {
        Id = Guid.NewGuid();
        LicensePlateNumberId = licensePlateNumberId;
        StockKeepingUnitId = stockKeepingUnitId;
        ExpirationDate = expirationDate;
        Quantity = quantity;
        CreatedAtUtc = createdAtUtc;
        CreatedBy = createdBy;
    }

    public Guid Id { get; private set; }
    public Guid LicensePlateNumberId { get; private set; }
    public LicensePlateNumber LicensePlateNumber { get; private set; } = null!;
    public Guid StockKeepingUnitId { get; private set; }
    public StockKeepingUnit StockKeepingUnit { get; private set; } = null!;
    public DateOnly? ExpirationDate { get; private set; }
    public decimal Quantity { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public DateTimeOffset? UpdatedAtUtc { get; private set; }
    public string? UpdatedBy { get; private set; }
    public DateTimeOffset? DeletedAtUtc { get; private set; }
    public string? DeletedBy { get; private set; }
    public bool IsDeleted => DeletedAtUtc.HasValue;

    internal OperationResult SetQuantity(decimal quantity, DateTimeOffset updatedAtUtc, string updatedBy) =>
        SetQuantityAndExpiration(quantity, ExpirationDate, updatedAtUtc, updatedBy);

    internal OperationResult SetQuantityAndExpiration(
        decimal quantity, DateOnly? expirationDate, DateTimeOffset updatedAtUtc, string updatedBy)
    {
        var validation = Validate(quantity, updatedAtUtc, updatedBy);
        if (!validation.IsSuccess)
            return validation;

        Quantity = quantity;
        ExpirationDate = expirationDate;
        UpdatedAtUtc = updatedAtUtc;
        UpdatedBy = updatedBy.Trim();
        DeletedAtUtc = null;
        DeletedBy = null;
        return OperationResult.Success();
    }

    internal OperationResult Delete(DateTimeOffset deletedAtUtc, string deletedBy)
    {
        if (IsDeleted)
            return OperationError.NotFound("Строка состава LPN уже удалена.");
        if (deletedAtUtc == default || string.IsNullOrWhiteSpace(deletedBy))
            return OperationError.Invalid("Для удаления строки LPN обязателен пользователь и время.");

        DeletedAtUtc = deletedAtUtc;
        DeletedBy = deletedBy.Trim();
        UpdatedAtUtc = deletedAtUtc;
        UpdatedBy = deletedBy.Trim();
        return OperationResult.Success();
    }

    internal static OperationResult Validate(decimal quantity, DateTimeOffset occurredAtUtc, string userId)
    {
        if (!WarehouseQuantity.IsNonNegative(quantity))
            return OperationError.Invalid("Количество в LPN должно быть конечным неотрицательным числом.");
        if (occurredAtUtc == default || string.IsNullOrWhiteSpace(userId))
            return OperationError.Invalid("Для изменения LPN обязателен пользователь и время.");
        return OperationResult.Success();
    }
}
