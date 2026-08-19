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
    public double Quantity { get; private set; }
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
        double quantity,
        DateTimeOffset createdAtUtc,
        RecorderType recorderType,
        Guid? recorderId,
        int? recorderLineNumber,
        string? confirmedBy = null)
    {
        if (id == Guid.Empty)
        {
            return OperationError.Invalid<InventoryMovement>("Inventory movement identifier is required.");
        }

        if (warehouseId == Guid.Empty)
        {
            return OperationError.Invalid<Warehouse>("Warehouse identifier is required.");
        }

        if (stockKeepingUnitId == Guid.Empty)
        {
            return OperationError.Invalid<StockKeepingUnit>("SKU identifier is required.");
        }

        var stateResult = ValidateState(sourceStorageLocationId, destinationStorageLocationId, quantity);
        if (!stateResult.IsSuccess)
        {
            return stateResult.Error!;
        }

        if (createdAtUtc == default)
        {
            return OperationError.Invalid<InventoryMovement>("Movement creation time is required.");
        }

        var recorderResult = ValidateRecorder(recorderType, recorderId, recorderLineNumber);
        if (!recorderResult.IsSuccess)
        {
            return recorderResult.Error!;
        }

        if (confirmedBy is not null && string.IsNullOrWhiteSpace(confirmedBy))
        {
            return OperationError.Invalid<InventoryMovement>("Confirming user cannot be empty.");
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
        double quantity,
        DateTimeOffset updatedAtUtc)
    {
        var draftResult = ValidateDraft();
        if (!draftResult.IsSuccess)
        {
            return draftResult;
        }

        if (stockKeepingUnitId == Guid.Empty)
        {
            return OperationError.Invalid<StockKeepingUnit>("SKU identifier is required.");
        }

        var stateResult = ValidateState(sourceStorageLocationId, destinationStorageLocationId, quantity);
        if (!stateResult.IsSuccess)
        {
            return stateResult;
        }

        if (updatedAtUtc == default || updatedAtUtc < CreatedAtUtc)
        {
            return OperationError.Invalid<InventoryMovement>(
                "Movement update time cannot precede its creation time.");
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
            return OperationError.Invalid<InventoryMovement>("Confirming user must be specified.");
        }

        ConfirmedBy = confirmedBy.Trim();
        return OperationResult.Success();
    }

    public OperationResult Post(DateTimeOffset postedAtUtc)
    {
        if (PostedAtUtc is not null)
        {
            return OperationError.Invalid<InventoryMovement>("Inventory movement has already been posted.");
        }

        var stateResult = ValidateState(SourceStorageLocationId, DestinationStorageLocationId, Quantity);
        if (!stateResult.IsSuccess)
        {
            return stateResult;
        }

        if (postedAtUtc == default || postedAtUtc < CreatedAtUtc || postedAtUtc < UpdatedAtUtc)
        {
            return OperationError.Invalid<InventoryMovement>(
                "Movement posting time cannot precede its changes.");
        }

        PostedAtUtc = postedAtUtc;
        return OperationResult.Success();
    }

    public OperationResult ValidateDraft()
    {
        return PostedAtUtc is null
            ? OperationResult.Success()
            : OperationError.Invalid<InventoryMovement>("Posted inventory movement cannot be changed.");
    }

    private static OperationResult ValidateState(
        Guid? sourceStorageLocationId,
        Guid? destinationStorageLocationId,
        double quantity)
    {
        if (!double.IsFinite(quantity) || quantity <= 0)
        {
            return OperationError.Invalid<InventoryMovement>(
                "Inventory movement quantity must be a finite number greater than zero.");
        }

        if (sourceStorageLocationId is null && destinationStorageLocationId is null)
        {
            return OperationError.Invalid<InventoryMovement>(
                "Inventory movement source or destination must be specified.");
        }

        if (sourceStorageLocationId == destinationStorageLocationId)
        {
            return OperationError.Invalid<InventoryMovement>(
                "Inventory movement source and destination must be different.");
        }

        if (sourceStorageLocationId == Guid.Empty || destinationStorageLocationId == Guid.Empty)
        {
            return OperationError.Invalid<StorageLocation>("Storage location identifier cannot be empty.");
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
                : OperationError.Invalid<InventoryMovement>(
                    "A movement without a recorder cannot have a recorder identifier or line number.");
        }

        return recorderId is not null && recorderId != Guid.Empty && recorderLineNumber > 0
            ? OperationResult.Success()
            : OperationError.Invalid<InventoryMovement>(
                "A recorded movement requires a recorder identifier and positive line number.");
    }
}
