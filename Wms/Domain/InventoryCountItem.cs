using Wms.Common;

namespace Wms.Domain;

public class InventoryCountItem
{
    private InventoryCountItem()
    {
    }

    public Guid Id { get; private set; }

    public Guid InventoryCountId { get; private set; }
    public InventoryCount? InventoryCount { get; private set; }

    public int LineNumber { get; private set; }

    public Guid? StorageLocationId { get; private set; }
    public StorageLocation? StorageLocation { get; private set; }

    public Guid? StockKeepingUnitId { get; private set; }
    public StockKeepingUnit? StockKeepingUnit { get; private set; }

    public double ExpectedQuantity { get; private set; }
    public double CountedQuantity { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public DateTimeOffset? UpdatedAtUtc { get; private set; }
    public string? UpdatedBy { get; private set; }

    public bool IsComplete => StorageLocationId.HasValue && StockKeepingUnitId.HasValue;
    public double DifferenceQuantity => CountedQuantity - ExpectedQuantity;
    public double? CountedWeightKg => WeightCalculation.CalculateKg(CountedQuantity, StockKeepingUnit);

    internal static OperationResult<InventoryCountItem> Create(
        Guid id,
        Guid inventoryCountId,
        int lineNumber,
        DateTimeOffset createdAtUtc,
        string createdBy)
    {
        if (id == Guid.Empty)
        {
            return OperationError.Invalid<InventoryCountItem>("Inventory count item identifier is required.");
        }

        if (inventoryCountId == Guid.Empty)
        {
            return OperationError.Invalid<InventoryCount>("Inventory count identifier is required.");
        }

        if (lineNumber <= 0)
        {
            return OperationError.Invalid<InventoryCountItem>("Line number must be positive.");
        }

        var auditResult = ValidateAudit(createdAtUtc, createdBy, "Creating user must be specified.");
        if (!auditResult.IsSuccess)
        {
            return auditResult.Error!;
        }

        return new InventoryCountItem
        {
            Id = id,
            InventoryCountId = inventoryCountId,
            LineNumber = lineNumber,
            CreatedAtUtc = createdAtUtc,
            CreatedBy = createdBy.Trim()
        };
    }

    internal OperationResult Update(
        Guid? storageLocationId,
        Guid? stockKeepingUnitId,
        double expectedQuantity,
        double countedQuantity,
        DateTimeOffset updatedAtUtc,
        string updatedBy)
    {
        if (storageLocationId == Guid.Empty)
        {
            return OperationError.Invalid<StorageLocation>("Storage location identifier is invalid.");
        }

        if (stockKeepingUnitId == Guid.Empty)
        {
            return OperationError.Invalid<StockKeepingUnit>("SKU identifier is invalid.");
        }

        if (!double.IsFinite(expectedQuantity) || expectedQuantity < 0)
        {
            return OperationError.Invalid<InventoryCountItem>(
                "Expected quantity must be a finite nonnegative number.");
        }

        if (!double.IsFinite(countedQuantity) || countedQuantity < 0)
        {
            return OperationError.Invalid<InventoryCountItem>(
                "Counted quantity must be a finite nonnegative number.");
        }

        var auditResult = ValidateAudit(updatedAtUtc, updatedBy, "Updating user must be specified.");
        if (!auditResult.IsSuccess)
        {
            return auditResult;
        }

        if (updatedAtUtc < CreatedAtUtc)
        {
            return OperationError.Invalid<InventoryCountItem>(
                "Update time cannot precede inventory count item creation.");
        }

        StorageLocationId = storageLocationId;
        StockKeepingUnitId = stockKeepingUnitId;
        ExpectedQuantity = expectedQuantity;
        CountedQuantity = countedQuantity;
        UpdatedAtUtc = updatedAtUtc;
        UpdatedBy = updatedBy.Trim();
        return OperationResult.Success();
    }

    private static OperationResult ValidateAudit(
        DateTimeOffset occurredAtUtc,
        string userId,
        string missingUserMessage)
    {
        if (occurredAtUtc == default)
        {
            return OperationError.Invalid<InventoryCountItem>("Operation time is required.");
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            return OperationError.Invalid<InventoryCountItem>(missingUserMessage);
        }

        return OperationResult.Success();
    }
}
