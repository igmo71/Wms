using Wms.Common;
using Wms.Domain.Enums;

namespace Wms.Domain;

public class InventoryCount
{
    private readonly List<InventoryCountItem> _items = [];

    private InventoryCount()
    {
    }

    public Guid Id { get; private set; }
    public string Number { get; private set; } = null!;
    public DateTime Date { get; private set; }

    public Guid WarehouseId { get; private set; }
    public Warehouse? Warehouse { get; private set; }

    public InventoryCountStatus Status { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public DateTimeOffset? UpdatedAtUtc { get; private set; }
    public string? UpdatedBy { get; private set; }
    public DateTimeOffset? PostedAtUtc { get; private set; }
    public string? PostedBy { get; private set; }

    public IReadOnlyCollection<InventoryCountItem> Items => _items;

    public static OperationResult<InventoryCount> Create(
        Guid id,
        string number,
        DateTime date,
        Guid warehouseId,
        DateTimeOffset createdAtUtc,
        string createdBy)
    {
        if (id == Guid.Empty)
        {
            return OperationError.Invalid("Идентификатор инвентаризации обязателен.");
        }

        if (string.IsNullOrWhiteSpace(number))
        {
            return OperationError.Invalid("Номер инвентаризации обязателен.");
        }

        if (date == default)
        {
            return OperationError.Invalid("Дата инвентаризации обязательна.");
        }

        if (warehouseId == Guid.Empty)
        {
            return OperationError.Invalid("Идентификатор склада обязателен.");
        }

        var auditResult = ValidateAudit(createdAtUtc, createdBy, "Creating user must be specified.");
        if (!auditResult.IsSuccess)
        {
            return auditResult.Error!;
        }

        return new InventoryCount
        {
            Id = id,
            Number = number.Trim(),
            Date = date.Date,
            WarehouseId = warehouseId,
            Status = InventoryCountStatus.Draft,
            CreatedAtUtc = createdAtUtc,
            CreatedBy = createdBy.Trim()
        };
    }

    public OperationResult<InventoryCountItem> AddItem(
        Guid itemId,
        DateTimeOffset createdAtUtc,
        string createdBy)
    {
        var draftResult = ValidateDraft("Items can be added only to a draft inventory count.");
        if (!draftResult.IsSuccess)
        {
            return draftResult.Error!;
        }

        var lineNumber = _items.Count == 0 ? 1 : _items.Max(x => x.LineNumber) + 1;
        var itemResult = InventoryCountItem.Create(
            itemId,
            Id,
            lineNumber,
            createdAtUtc,
            createdBy);
        if (!itemResult.IsSuccess)
        {
            return itemResult.Error!;
        }

        _items.Add(itemResult.Value!);
        Touch(createdAtUtc, createdBy);
        return itemResult;
    }

    public OperationResult UpdateItem(
        Guid itemId,
        Guid? storageLocationId,
        Guid? stockKeepingUnitId,
        double expectedQuantity,
        double countedQuantity,
        DateTimeOffset updatedAtUtc,
        string updatedBy)
    {
        var draftResult = ValidateDraft("Items can be changed only in a draft inventory count.");
        if (!draftResult.IsSuccess)
        {
            return draftResult;
        }

        var item = _items.FirstOrDefault(x => x.Id == itemId);
        if (item is null)
        {
            return OperationError.NotFound();
        }

        if (storageLocationId.HasValue
            && stockKeepingUnitId.HasValue
            && _items.Any(x => x.Id != itemId
                && x.StorageLocationId == storageLocationId
                && x.StockKeepingUnitId == stockKeepingUnitId))
        {
            return OperationError.Invalid(
                "Сочетание складской позиции и номенклатуры не должно повторяться в инвентаризации.");
        }

        var updateResult = item.Update(
            storageLocationId,
            stockKeepingUnitId,
            expectedQuantity,
            countedQuantity,
            updatedAtUtc,
            updatedBy);
        if (!updateResult.IsSuccess)
        {
            return updateResult;
        }

        Touch(updatedAtUtc, updatedBy);
        return OperationResult.Success();
    }

    public OperationResult RemoveItem(
        Guid itemId,
        DateTimeOffset removedAtUtc,
        string removedBy)
    {
        var draftResult = ValidateDraft("Items can be deleted only from a draft inventory count.");
        if (!draftResult.IsSuccess)
        {
            return draftResult;
        }

        var item = _items.FirstOrDefault(x => x.Id == itemId);
        if (item is null)
        {
            return OperationError.NotFound();
        }

        var auditResult = ValidateAudit(removedAtUtc, removedBy, "Deleting user must be specified.");
        if (!auditResult.IsSuccess)
        {
            return auditResult;
        }

        _items.Remove(item);
        Touch(removedAtUtc, removedBy);
        return OperationResult.Success();
    }

    public OperationResult Post(DateTimeOffset postedAtUtc, string postedBy)
    {
        var draftResult = ValidateDraft("Only a draft inventory count can be posted.");
        if (!draftResult.IsSuccess)
        {
            return draftResult;
        }

        if (_items.Any(x => !x.IsComplete))
        {
            return OperationError.Invalid(
                "Перед проведением в каждой строке должны быть указаны складская позиция и номенклатура.");
        }

        var hasDuplicates = _items
            .GroupBy(x => new { x.StorageLocationId, x.StockKeepingUnitId })
            .Any(x => x.Count() > 1);
        if (hasDuplicates)
        {
            return OperationError.Invalid(
                "Сочетание складской позиции и номенклатуры не должно повторяться в инвентаризации.");
        }

        var auditResult = ValidateAudit(postedAtUtc, postedBy, "Posting user must be specified.");
        if (!auditResult.IsSuccess)
        {
            return auditResult;
        }

        if (postedAtUtc < CreatedAtUtc
            || _items.Any(x => x.CreatedAtUtc > postedAtUtc || x.UpdatedAtUtc > postedAtUtc))
        {
            return OperationError.Invalid(
                "Время проведения не может предшествовать последнему изменению инвентаризации.");
        }

        Status = InventoryCountStatus.Posted;
        PostedAtUtc = postedAtUtc;
        PostedBy = postedBy.Trim();
        Touch(postedAtUtc, postedBy);
        return OperationResult.Success();
    }

    private OperationResult ValidateDraft(string message)
    {
        return Status == InventoryCountStatus.Draft
            ? OperationResult.Success()
            : OperationError.Invalid(message);
    }

    private void Touch(DateTimeOffset updatedAtUtc, string updatedBy)
    {
        UpdatedAtUtc = updatedAtUtc;
        UpdatedBy = updatedBy.Trim();
    }

    private static OperationResult ValidateAudit(
        DateTimeOffset occurredAtUtc,
        string userId,
        string missingUserMessage)
    {
        if (occurredAtUtc == default)
        {
            return OperationError.Invalid("Время операции обязательно.");
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            return OperationError.Invalid(missingUserMessage);
        }

        return OperationResult.Success();
    }
}
