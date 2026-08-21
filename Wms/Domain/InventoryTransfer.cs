using Wms.Common;
using Wms.Domain.Enums;

namespace Wms.Domain;

public class InventoryTransfer
{
    private InventoryTransfer()
    {
    }

    public Guid Id { get; private set; }
    public string Number { get; private set; } = null!;
    public DateTime Date { get; private set; }

    public Guid WarehouseId { get; private set; }
    public Warehouse? Warehouse { get; private set; }

    public Guid? TransitStorageLocationId { get; private set; }
    public StorageLocation? TransitStorageLocation { get; private set; }

    public InventoryTransferStatus Status { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }
    public DateTimeOffset? StartedAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }

    public string CreatedBy { get; private set; } = null!;
    public string? StartedBy { get; private set; }
    public string? CompletedBy { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public static OperationResult<InventoryTransfer> Create(
        Guid id,
        string number,
        DateTime date,
        Guid warehouseId,
        Guid? transitStorageLocationId,
        DateTimeOffset createdAtUtc,
        string createdBy)
    {
        if (id == Guid.Empty)
        {
            return OperationError.Invalid("Идентификатор перемещения обязателен.");
        }

        if (string.IsNullOrWhiteSpace(number))
        {
            return OperationError.Invalid("Номер перемещения обязателен.");
        }

        if (date == default)
        {
            return OperationError.Invalid("Дата перемещения обязательна.");
        }

        if (warehouseId == Guid.Empty)
        {
            return OperationError.Invalid("Идентификатор склада обязателен.");
        }

        if (transitStorageLocationId == Guid.Empty)
        {
            return OperationError.Invalid("Некорректный идентификатор транзитной позиции.");
        }

        var auditResult = ValidateAudit(createdAtUtc, createdBy, "Creating user must be specified.");
        if (!auditResult.IsSuccess)
        {
            return auditResult.Error!;
        }

        return new InventoryTransfer
        {
            Id = id,
            Number = number.Trim(),
            Date = date.Date,
            WarehouseId = warehouseId,
            TransitStorageLocationId = transitStorageLocationId,
            Status = InventoryTransferStatus.Draft,
            CreatedAtUtc = createdAtUtc,
            CreatedBy = createdBy.Trim()
        };
    }

    public OperationResult<InventoryTransferRoute> CreatePickRoute(Guid sourceStorageLocationId)
    {
        if (TransitStorageLocationId is not Guid transitStorageLocationId)
        {
            return OperationError.Invalid(
                "Перед отбором необходимо назначить транзитную позицию.");
        }

        return CreateRoute(sourceStorageLocationId, transitStorageLocationId);
    }

    public OperationResult<InventoryTransferRoute> CreatePutRoute(Guid destinationStorageLocationId)
    {
        if (TransitStorageLocationId is not Guid transitStorageLocationId)
        {
            return OperationError.Invalid(
                "Перед размещением необходимо назначить транзитную позицию.");
        }

        return CreateRoute(transitStorageLocationId, destinationStorageLocationId);
    }

    public OperationResult<InventoryTransferRoute> CreateDirectRoute(
        Guid sourceStorageLocationId,
        Guid destinationStorageLocationId) =>
        CreateRoute(sourceStorageLocationId, destinationStorageLocationId);

    public OperationResult RecordMovement(DateTimeOffset occurredAtUtc, string confirmedBy)
    {
        if (Status == InventoryTransferStatus.Completed)
        {
            return OperationError.Invalid(
                "Завершённое перемещение нельзя изменить.");
        }

        var auditResult = ValidateAudit(
            occurredAtUtc,
            confirmedBy,
            "Confirming user must be specified.");
        if (!auditResult.IsSuccess)
        {
            return auditResult;
        }

        if (occurredAtUtc < CreatedAtUtc)
        {
            return OperationError.Invalid(
                "Время движения не может предшествовать созданию перемещения.");
        }

        if (Status == InventoryTransferStatus.Draft)
        {
            Status = InventoryTransferStatus.InProgress;
            StartedAtUtc = occurredAtUtc;
            StartedBy = confirmedBy.Trim();
        }

        UpdatedAtUtc = occurredAtUtc;
        return OperationResult.Success();
    }

    public OperationResult Complete(DateTimeOffset completedAtUtc, string completedBy)
    {
        if (Status != InventoryTransferStatus.InProgress)
        {
            return OperationError.Invalid(
                "Завершить можно только перемещение в работе.");
        }

        var auditResult = ValidateAudit(
            completedAtUtc,
            completedBy,
            "Completing user must be specified.");
        if (!auditResult.IsSuccess)
        {
            return auditResult;
        }

        if (completedAtUtc < StartedAtUtc)
        {
            return OperationError.Invalid(
                "Время завершения не может предшествовать началу перемещения.");
        }

        Status = InventoryTransferStatus.Completed;
        UpdatedAtUtc = completedAtUtc;
        CompletedAtUtc = completedAtUtc;
        CompletedBy = completedBy.Trim();
        return OperationResult.Success();
    }

    public OperationResult ValidateDeletion()
    {
        return Status == InventoryTransferStatus.Draft
            ? OperationResult.Success()
            : OperationError.Invalid(
                "Удалить можно только черновик перемещения.");
    }

    private OperationResult<InventoryTransferRoute> CreateRoute(
        Guid sourceStorageLocationId,
        Guid destinationStorageLocationId)
    {
        if (Status == InventoryTransferStatus.Completed)
        {
            return OperationError.Invalid(
                "Завершённое перемещение нельзя изменить.");
        }

        if (sourceStorageLocationId == Guid.Empty || destinationStorageLocationId == Guid.Empty)
        {
            return OperationError.Invalid(
                "Необходимо указать позиции источника и назначения.");
        }

        if (sourceStorageLocationId == destinationStorageLocationId)
        {
            return OperationError.Invalid(
                "Позиции источника и назначения должны различаться.");
        }

        return new InventoryTransferRoute(sourceStorageLocationId, destinationStorageLocationId);
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

public sealed class InventoryTransferRoute
{
    internal InventoryTransferRoute(
        Guid sourceStorageLocationId,
        Guid destinationStorageLocationId)
    {
        SourceStorageLocationId = sourceStorageLocationId;
        DestinationStorageLocationId = destinationStorageLocationId;
    }

    public Guid SourceStorageLocationId { get; }
    public Guid DestinationStorageLocationId { get; }
}
