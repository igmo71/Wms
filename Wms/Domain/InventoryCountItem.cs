using Wms.Common;

namespace Wms.Domain;

public class InventoryCountItem
{
    private InventoryCountItem() { }

    public Guid Id { get; private set; }
    public Guid InventoryCountId { get; private set; }
    public InventoryCount? InventoryCount { get; private set; }
    public int LineNumber { get; private set; }
    public Guid StockKeepingUnitId { get; private set; }
    public StockKeepingUnit? StockKeepingUnit { get; private set; }
    public decimal ExpectedQuantity { get; private set; }
    public decimal? CountedQuantity { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public DateTimeOffset? UpdatedAtUtc { get; private set; }
    public string? UpdatedBy { get; private set; }

    public bool IsExpected => ExpectedQuantity > 0;
    public bool IsCounted => CountedQuantity.HasValue;
    public decimal? DifferenceQuantity => CountedQuantity - ExpectedQuantity;
    public double? CountedWeightKg => CountedQuantity.HasValue
        ? WeightCalculation.CalculateKg(CountedQuantity.Value, StockKeepingUnit)
        : null;

    internal static OperationResult<InventoryCountItem> Create(
        Guid id,
        Guid inventoryCountId,
        int lineNumber,
        Guid stockKeepingUnitId,
        decimal expectedQuantity,
        decimal? countedQuantity,
        DateTimeOffset createdAtUtc,
        string createdBy)
    {
        if (id == Guid.Empty || inventoryCountId == Guid.Empty)
            return OperationError.Invalid("Идентификаторы строки инвентаризации обязательны.");
        if (lineNumber <= 0)
            return OperationError.Invalid("Номер строки должен быть положительным.");
        if (stockKeepingUnitId == Guid.Empty)
            return OperationError.Invalid("Номенклатура строки инвентаризации обязательна.");

        var quantitiesResult = ValidateQuantities(expectedQuantity, countedQuantity);
        if (!quantitiesResult.IsSuccess)
            return quantitiesResult.Error!;

        var auditResult = ValidateAudit(createdAtUtc, createdBy);
        if (!auditResult.IsSuccess)
            return auditResult.Error!;

        return new InventoryCountItem
        {
            Id = id,
            InventoryCountId = inventoryCountId,
            LineNumber = lineNumber,
            StockKeepingUnitId = stockKeepingUnitId,
            ExpectedQuantity = expectedQuantity,
            CountedQuantity = countedQuantity,
            CreatedAtUtc = createdAtUtc,
            CreatedBy = createdBy.Trim()
        };
    }

    internal OperationResult IncrementCountedQuantity(DateTimeOffset updatedAtUtc, string updatedBy)
    {
        var auditResult = ValidateUpdateAudit(updatedAtUtc, updatedBy);
        if (!auditResult.IsSuccess)
            return auditResult;

        var countedQuantity = (CountedQuantity ?? 0) + 1;
        if (!WarehouseQuantity.IsNonNegative(countedQuantity))
            return OperationError.Invalid("Фактическое количество стало слишком большим.");

        CountedQuantity = countedQuantity;
        UpdatedAtUtc = updatedAtUtc;
        UpdatedBy = updatedBy.Trim();
        return OperationResult.Success();
    }

    internal OperationResult SetCountedQuantity(
        decimal countedQuantity,
        DateTimeOffset updatedAtUtc,
        string updatedBy)
    {
        var quantitiesResult = ValidateQuantities(ExpectedQuantity, countedQuantity);
        if (!quantitiesResult.IsSuccess)
            return quantitiesResult;

        var auditResult = ValidateUpdateAudit(updatedAtUtc, updatedBy);
        if (!auditResult.IsSuccess)
            return auditResult;

        CountedQuantity = countedQuantity;
        UpdatedAtUtc = updatedAtUtc;
        UpdatedBy = updatedBy.Trim();
        return OperationResult.Success();
    }

    private OperationResult ValidateUpdateAudit(DateTimeOffset updatedAtUtc, string updatedBy)
    {
        var auditResult = ValidateAudit(updatedAtUtc, updatedBy);
        if (!auditResult.IsSuccess)
            return auditResult;

        return updatedAtUtc < CreatedAtUtc
            ? OperationError.Invalid("Время изменения не может предшествовать созданию строки инвентаризации.")
            : OperationResult.Success();
    }

    private static OperationResult ValidateQuantities(decimal expectedQuantity, decimal? countedQuantity)
    {
        if (!WarehouseQuantity.IsNonNegative(expectedQuantity))
            return OperationError.Invalid("Ожидаемое количество должно быть конечным неотрицательным числом.");
        if (countedQuantity is decimal quantity && !WarehouseQuantity.IsNonNegative(quantity))
            return OperationError.Invalid("Фактическое количество должно быть конечным неотрицательным числом.");
        return OperationResult.Success();
    }

    private static OperationResult ValidateAudit(DateTimeOffset occurredAtUtc, string userId)
    {
        if (occurredAtUtc == default)
            return OperationError.Invalid("Время операции обязательно.");
        return string.IsNullOrWhiteSpace(userId)
            ? OperationError.Invalid("Пользователь операции обязателен.")
            : OperationResult.Success();
    }
}
