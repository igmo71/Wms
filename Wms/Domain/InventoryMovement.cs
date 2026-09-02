using Wms.Common;
using Wms.Domain.Enums;

namespace Wms.Domain;

public class InventoryMovement
{
    private InventoryMovement()
    {
    }

    public Guid Id { get; private set; }
    public Guid WarehouseId { get; private set; }
    public Warehouse? Warehouse { get; private set; }
    public Guid? SourceStorageLocationId { get; private set; }
    public StorageLocation? SourceStorageLocation { get; private set; }
    public Guid? DestinationStorageLocationId { get; private set; }
    public StorageLocation? DestinationStorageLocation { get; private set; }
    public Guid StockKeepingUnitId { get; private set; }
    public StockKeepingUnit? StockKeepingUnit { get; private set; }
    public decimal Quantity { get; private set; }
    public double? WeightKg => WeightCalculation.CalculateKg(Quantity, StockKeepingUnit);
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }
    public DateTimeOffset? PostedAtUtc { get; private set; }
    public string? ConfirmedBy { get; private set; }
    public Guid? RecorderId { get; private set; }
    public int? RecorderLineNumber { get; private set; }
    public RecorderType RecorderType { get; private set; }

    public static OperationResult<InventoryMovement> Create(
        Guid id,
        Guid warehouseId,
        Guid? sourceStorageLocationId,
        Guid? destinationStorageLocationId,
        Guid stockKeepingUnitId,
        decimal quantity,
        DateTimeOffset createdAtUtc,
        RecorderType recorderType,
        Guid? recorderId,
        int? recorderLineNumber,
        string? confirmedBy = null)
    {
        if (id == Guid.Empty)
        {
            return OperationError.Invalid("Идентификатор движения обязателен.");
        }

        if (warehouseId == Guid.Empty)
        {
            return OperationError.Invalid("Идентификатор склада обязателен.");
        }

        if (stockKeepingUnitId == Guid.Empty)
        {
            return OperationError.Invalid("Идентификатор номенклатуры обязателен.");
        }

        var stateResult = ValidateState(sourceStorageLocationId, destinationStorageLocationId, quantity);
        if (!stateResult.IsSuccess)
        {
            return stateResult.Error!;
        }

        if (createdAtUtc == default)
        {
            return OperationError.Invalid("Время создания движения обязательно.");
        }

        var recorderResult = ValidateRecorder(recorderType, recorderId, recorderLineNumber);
        if (!recorderResult.IsSuccess)
        {
            return recorderResult.Error!;
        }

        if (confirmedBy is not null && string.IsNullOrWhiteSpace(confirmedBy))
        {
            return OperationError.Invalid("Пользователь подтверждения не может быть пустым.");
        }

        return new InventoryMovement
        {
            Id = id,
            WarehouseId = warehouseId,
            SourceStorageLocationId = sourceStorageLocationId,
            DestinationStorageLocationId = destinationStorageLocationId,
            StockKeepingUnitId = stockKeepingUnitId,
            Quantity = quantity,
            CreatedAtUtc = createdAtUtc,
            ConfirmedBy = confirmedBy?.Trim(),
            RecorderType = recorderType,
            RecorderId = recorderId,
            RecorderLineNumber = recorderLineNumber
        };
    }

    public OperationResult UpdateDraft(
        Guid? sourceStorageLocationId,
        Guid? destinationStorageLocationId,
        Guid stockKeepingUnitId,
        decimal quantity,
        DateTimeOffset updatedAtUtc)
    {
        var draftResult = ValidateDraft();
        if (!draftResult.IsSuccess)
        {
            return draftResult;
        }

        if (stockKeepingUnitId == Guid.Empty)
        {
            return OperationError.Invalid("Идентификатор номенклатуры обязателен.");
        }

        var stateResult = ValidateState(sourceStorageLocationId, destinationStorageLocationId, quantity);
        if (!stateResult.IsSuccess)
        {
            return stateResult;
        }

        if (updatedAtUtc == default || updatedAtUtc < CreatedAtUtc)
        {
            return OperationError.Invalid(
                "Время изменения движения не может предшествовать времени его создания.");
        }

        SourceStorageLocationId = sourceStorageLocationId;
        DestinationStorageLocationId = destinationStorageLocationId;
        StockKeepingUnitId = stockKeepingUnitId;
        Quantity = quantity;
        UpdatedAtUtc = updatedAtUtc;
        return OperationResult.Success();
    }

    public OperationResult Confirm(string confirmedBy)
    {
        var draftResult = ValidateDraft();
        if (!draftResult.IsSuccess)
        {
            return draftResult;
        }

        if (string.IsNullOrWhiteSpace(confirmedBy))
        {
            return OperationError.Invalid("Необходимо указать пользователя подтверждения.");
        }

        ConfirmedBy = confirmedBy.Trim();
        return OperationResult.Success();
    }

    public OperationResult Post(DateTimeOffset postedAtUtc)
    {
        if (PostedAtUtc is not null)
        {
            return OperationError.Invalid("Движение уже проведено.");
        }

        var stateResult = ValidateState(SourceStorageLocationId, DestinationStorageLocationId, Quantity);
        if (!stateResult.IsSuccess)
        {
            return stateResult;
        }

        if (postedAtUtc == default || postedAtUtc < CreatedAtUtc || postedAtUtc < UpdatedAtUtc)
        {
            return OperationError.Invalid(
                "Время проведения движения не может предшествовать времени его изменения.");
        }

        PostedAtUtc = postedAtUtc;
        return OperationResult.Success();
    }

    public OperationResult ValidateDraft()
    {
        return PostedAtUtc is null
            ? OperationResult.Success()
            : OperationError.Invalid("Проведённое движение нельзя изменить.");
    }

    private static OperationResult ValidateState(
        Guid? sourceStorageLocationId,
        Guid? destinationStorageLocationId,
        decimal quantity)
    {
        if (!WarehouseQuantity.IsPositive(quantity))
        {
            return OperationError.Invalid(
                "Количество движения должно быть конечным числом больше нуля.");
        }

        if (sourceStorageLocationId is null && destinationStorageLocationId is null)
        {
            return OperationError.Invalid(
                "Необходимо указать источник или назначение движения.");
        }

        if (sourceStorageLocationId == destinationStorageLocationId)
        {
            return OperationError.Invalid(
                "Источник и назначение движения должны различаться.");
        }

        if (sourceStorageLocationId == Guid.Empty || destinationStorageLocationId == Guid.Empty)
        {
            return OperationError.Invalid("Идентификатор складской позиции не может быть пустым.");
        }

        return OperationResult.Success();
    }

    private static OperationResult ValidateRecorder(
        RecorderType recorderType,
        Guid? recorderId,
        int? recorderLineNumber)
    {
        if (recorderType == RecorderType.Unknown)
        {
            return recorderId is null && recorderLineNumber is null
                ? OperationResult.Success()
                : OperationError.Invalid(
                    "У движения без регистратора не должно быть его идентификатора или номера строки.");
        }

        return recorderId is not null && recorderId != Guid.Empty && recorderLineNumber > 0
            ? OperationResult.Success()
            : OperationError.Invalid(
                "Для движения с регистратором обязательны его идентификатор и положительный номер строки.");
    }
}
