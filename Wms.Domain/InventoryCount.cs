using Wms.Common;
using Wms.Domain.Enums;

namespace Wms.Domain;

public class InventoryCount
{
    private readonly List<InventoryCountItem> _items = [];

    private InventoryCount() { }

    public Guid Id { get; private set; }
    public string Number { get; private set; } = null!;
    public DateTime Date { get; private set; }
    public Guid WarehouseId { get; private set; }
    public Warehouse? Warehouse { get; private set; }
    public Guid StorageLocationId { get; private set; }
    public StorageLocation? StorageLocation { get; private set; }
    public InventoryCountStatus Status { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public DateTimeOffset? UpdatedAtUtc { get; private set; }
    public string? UpdatedBy { get; private set; }
    public DateTimeOffset? PostedAtUtc { get; private set; }
    public string? PostedBy { get; private set; }
    public byte[] RowVersion { get; private set; } = null!;
    public IReadOnlyCollection<InventoryCountItem> Items => _items;

    public static OperationResult<InventoryCount> Create(
        Guid id,
        string number,
        DateTime date,
        Guid warehouseId,
        Guid storageLocationId,
        DateTimeOffset createdAtUtc,
        string createdBy)
    {
        if (id == Guid.Empty || warehouseId == Guid.Empty || storageLocationId == Guid.Empty)
            return OperationError.Invalid("Идентификаторы инвентаризации, склада и ячейки обязательны.");
        if (string.IsNullOrWhiteSpace(number) || date == default)
            return OperationError.Invalid("Номер и дата инвентаризации обязательны.");

        var auditResult = ValidateAudit(createdAtUtc, createdBy);
        if (!auditResult.IsSuccess)
            return auditResult.Error!;

        return new InventoryCount
        {
            Id = id,
            Number = number.Trim(),
            Date = date.Date,
            WarehouseId = warehouseId,
            StorageLocationId = storageLocationId,
            Status = InventoryCountStatus.Draft,
            CreatedAtUtc = createdAtUtc,
            CreatedBy = createdBy.Trim()
        };
    }

    public OperationResult<InventoryCountItem> AddExpectedItem(
        Guid itemId,
        Guid stockKeepingUnitId,
        decimal expectedQuantity,
        DateTimeOffset createdAtUtc,
        string createdBy)
    {
        if (expectedQuantity <= 0)
            return OperationError.Invalid("Ожидаемая строка должна иметь положительный остаток.");
        return AddItem(itemId, stockKeepingUnitId, expectedQuantity, null, createdAtUtc, createdBy);
    }

    public OperationResult<InventoryCountItem> IncrementSku(
        Guid itemId,
        Guid stockKeepingUnitId,
        DateTimeOffset updatedAtUtc,
        string updatedBy)
    {
        var draftResult = ValidateDraft();
        if (!draftResult.IsSuccess)
            return draftResult.Error!;

        var existingItem = _items.SingleOrDefault(x => x.StockKeepingUnitId == stockKeepingUnitId);
        if (existingItem is null)
            return AddItem(itemId, stockKeepingUnitId, 0, 1, updatedAtUtc, updatedBy);

        var incrementResult = existingItem.IncrementCountedQuantity(updatedAtUtc, updatedBy);
        if (!incrementResult.IsSuccess)
            return incrementResult.Error!;

        Touch(updatedAtUtc, updatedBy);
        return existingItem;
    }

    public OperationResult SetCountedQuantity(
        Guid itemId,
        decimal countedQuantity,
        DateTimeOffset updatedAtUtc,
        string updatedBy)
    {
        var itemResult = FindDraftItem(itemId);
        if (!itemResult.IsSuccess)
            return itemResult.Error!;

        var updateResult = itemResult.Value!.SetCountedQuantity(countedQuantity, updatedAtUtc, updatedBy);
        if (!updateResult.IsSuccess)
            return updateResult;

        Touch(updatedAtUtc, updatedBy);
        return OperationResult.Success();
    }

    public OperationResult<InventoryCountItem> SetSkuCountedQuantity(
        Guid itemId,
        Guid stockKeepingUnitId,
        decimal countedQuantity,
        DateTimeOffset updatedAtUtc,
        string updatedBy)
    {
        var draftResult = ValidateDraft();
        if (!draftResult.IsSuccess)
            return draftResult.Error!;

        var existingItem = _items.SingleOrDefault(x => x.StockKeepingUnitId == stockKeepingUnitId);
        if (existingItem is null)
            return AddItem(itemId, stockKeepingUnitId, 0, countedQuantity, updatedAtUtc, updatedBy);

        var updateResult = existingItem.SetCountedQuantity(countedQuantity, updatedAtUtc, updatedBy);
        if (!updateResult.IsSuccess)
            return updateResult.Error!;

        Touch(updatedAtUtc, updatedBy);
        return existingItem;
    }

    public OperationResult RemoveUnexpectedItem(Guid itemId, DateTimeOffset removedAtUtc, string removedBy)
    {
        var itemResult = FindDraftItem(itemId);
        if (!itemResult.IsSuccess)
            return itemResult.Error!;

        var item = itemResult.Value!;
        if (item.IsExpected)
            return OperationError.Invalid("Ожидаемую позицию нельзя удалить из инвентаризации.");

        var auditResult = ValidateAudit(removedAtUtc, removedBy);
        if (!auditResult.IsSuccess)
            return auditResult;

        _items.Remove(item);
        Touch(removedAtUtc, removedBy);
        return OperationResult.Success();
    }

    public OperationResult Post(DateTimeOffset postedAtUtc, string postedBy)
    {
        var draftResult = ValidateDraft();
        if (!draftResult.IsSuccess)
            return draftResult;
        if (_items.Any(x => !x.IsCounted))
            return OperationError.Invalid("Перед проведением пересчитайте каждую ожидаемую позицию.");

        var auditResult = ValidateAudit(postedAtUtc, postedBy);
        if (!auditResult.IsSuccess)
            return auditResult;
        if (postedAtUtc < CreatedAtUtc
            || _items.Any(x => x.CreatedAtUtc > postedAtUtc || x.UpdatedAtUtc > postedAtUtc))
            return OperationError.Invalid("Время проведения не может предшествовать последнему изменению инвентаризации.");

        Status = InventoryCountStatus.Posted;
        PostedAtUtc = postedAtUtc;
        PostedBy = postedBy.Trim();
        Touch(postedAtUtc, postedBy);
        return OperationResult.Success();
    }

    private OperationResult<InventoryCountItem> AddItem(
        Guid itemId,
        Guid stockKeepingUnitId,
        decimal expectedQuantity,
        decimal? countedQuantity,
        DateTimeOffset createdAtUtc,
        string createdBy)
    {
        var draftResult = ValidateDraft();
        if (!draftResult.IsSuccess)
            return draftResult.Error!;
        if (_items.Any(x => x.StockKeepingUnitId == stockKeepingUnitId))
            return OperationError.Conflict("Товар уже присутствует в инвентаризации.");

        var lineNumber = _items.Count == 0 ? 1 : _items.Max(x => x.LineNumber) + 1;
        var itemResult = InventoryCountItem.Create(
            itemId,
            Id,
            lineNumber,
            stockKeepingUnitId,
            expectedQuantity,
            countedQuantity,
            createdAtUtc,
            createdBy);
        if (!itemResult.IsSuccess)
            return itemResult.Error!;

        _items.Add(itemResult.Value!);
        Touch(createdAtUtc, createdBy);
        return itemResult;
    }

    private OperationResult<InventoryCountItem> FindDraftItem(Guid itemId)
    {
        var draftResult = ValidateDraft();
        if (!draftResult.IsSuccess)
            return draftResult.Error!;
        var item = _items.SingleOrDefault(x => x.Id == itemId);
        return item is null
            ? OperationError.NotFound($"Строка инвентаризации '{itemId}' не найдена.")
            : item;
    }

    private OperationResult ValidateDraft() => Status == InventoryCountStatus.Draft
        ? OperationResult.Success()
        : OperationError.Invalid("Изменять можно только черновик инвентаризации.");

    private void Touch(DateTimeOffset updatedAtUtc, string updatedBy)
    {
        UpdatedAtUtc = updatedAtUtc;
        UpdatedBy = updatedBy.Trim();
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
