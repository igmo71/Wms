using Wms.Common;
using Wms.Domain.Enums;

namespace Wms.Domain;

public sealed class ReplenishmentTask
{
    private readonly List<ReplenishmentSourceCycle> _sourceCycles = [];
    private ReplenishmentTask() { }

    public Guid Id { get; private set; }
    public Guid PickingAssemblyId { get; private set; }
    public Guid WarehouseId { get; private set; }
    public Guid StockKeepingUnitId { get; private set; }
    public decimal OrderedQuantity { get; private set; }
    public decimal TransferredQuantity { get; private set; }
    public decimal RemainingQuantity => OrderedQuantity - TransferredQuantity;
    public Guid? RecommendedSourceStorageLocationId { get; private set; }
    public Guid? RecommendedDestinationStorageLocationId { get; private set; }
    public ReplenishmentTaskStatus Status { get; private set; }
    public string OrderedBy { get; private set; } = null!;
    public string? AssignedTo { get; private set; }
    public DateTimeOffset OrderedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public DateTimeOffset? CancelledAtUtc { get; private set; }
    public string? CancelledBy { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public IReadOnlyCollection<ReplenishmentSourceCycle> SourceCycles => _sourceCycles;

    public static OperationResult<ReplenishmentTask> Order(
        Guid id, Guid assemblyId, Guid warehouseId, Guid skuId, decimal quantity,
        Guid? recommendedSourceId, Guid? recommendedDestinationId,
        string userId, DateTimeOffset orderedAtUtc)
    {
        if (id == Guid.Empty || assemblyId == Guid.Empty || warehouseId == Guid.Empty || skuId == Guid.Empty
            || recommendedSourceId == Guid.Empty || recommendedDestinationId == Guid.Empty)
            return OperationError.Invalid("Идентификаторы задания Подпитки указаны некорректно.");
        if (!WarehouseQuantity.IsPositive(quantity))
            return OperationError.Invalid("Заказ Подпитки должен быть положительным с точностью до 0,001.");
        if (string.IsNullOrWhiteSpace(userId) || userId.Length > 36 || orderedAtUtc == default)
            return OperationError.Invalid("Для заказа Подпитки обязательны пользователь и время.");

        return new ReplenishmentTask
        {
            Id = id,
            PickingAssemblyId = assemblyId,
            WarehouseId = warehouseId,
            StockKeepingUnitId = skuId,
            OrderedQuantity = quantity,
            RecommendedSourceStorageLocationId = recommendedSourceId,
            RecommendedDestinationStorageLocationId = recommendedDestinationId,
            OrderedBy = userId.Trim(),
            OrderedAtUtc = orderedAtUtc,
            UpdatedAtUtc = orderedAtUtc,
            Status = ReplenishmentTaskStatus.Ordered
        };
    }

    public OperationResult<ReplenishmentSourceCycle> StartSource(
        Guid cycleId, Guid sourceId, string userId, DateTimeOffset startedAtUtc)
    {
        if (Status is ReplenishmentTaskStatus.Completed or ReplenishmentTaskStatus.Cancelled
            || RemainingQuantity == 0)
            return OperationError.Conflict("Задание Подпитки больше не требует нового источника.");
        if (_sourceCycles.Any(x => x.CompletedAtUtc is null))
            return OperationError.Conflict("Сначала завершите работу с уже снятым поддоном.");
        if (AssignedTo is not null && AssignedTo != userId)
            return OperationError.Conflict("Задание выполняется другим подпитчиком.");
        if (startedAtUtc == default || startedAtUtc < UpdatedAtUtc)
            return OperationError.Invalid("Время начала предшествует последнему действию задания.");
        if (_sourceCycles.Any(x => x.Id == cycleId))
            return OperationError.Conflict("Цикл источника уже сохранён.");

        var result = ReplenishmentSourceCycle.Create(cycleId, Id, sourceId, userId, startedAtUtc);
        if (!result.IsSuccess)
            return result.Error!;

        _sourceCycles.Add(result.Value!);
        AssignedTo = userId.Trim();
        UpdatedAtUtc = startedAtUtc;
        Status = ReplenishmentTaskStatus.InProgress;
        return result.Value!;
    }

    public OperationResult<ReplenishmentTransfer> ConfirmTransfer(
        Guid transferId, Guid cycleId, Guid destinationId, DateOnly? expirationDate,
        decimal quantity, Guid movementId, string userId, DateTimeOffset confirmedAtUtc)
    {
        if (Status != ReplenishmentTaskStatus.InProgress || AssignedTo != userId)
            return OperationError.Conflict("Подтвердить Подпитку может исполнитель начатого задания.");
        if (!WarehouseQuantity.IsPositive(quantity) || quantity > RemainingQuantity)
            return OperationError.Invalid("Количество превышает невыполненную часть Подпитки или имеет неверную точность.");
        if (confirmedAtUtc == default || confirmedAtUtc < UpdatedAtUtc)
            return OperationError.Invalid("Время подтверждения предшествует последнему действию задания.");
        var cycle = _sourceCycles.SingleOrDefault(x => x.Id == cycleId);
        if (cycle is null)
            return OperationError.NotFound("Цикл источника Подпитки не найден.");
        if (_sourceCycles.SelectMany(x => x.Transfers)
            .Any(x => x.Id == transferId || x.InventoryMovementId == movementId))
            return OperationError.Conflict("Шаг Подпитки уже сохранён.");

        var result = ReplenishmentTransfer.Create(transferId, cycleId, destinationId,
            expirationDate, quantity, movementId, userId, confirmedAtUtc);
        if (!result.IsSuccess)
            return result.Error!;
        var addResult = cycle.AddTransfer(result.Value!);
        if (!addResult.IsSuccess)
            return addResult.Error!;

        TransferredQuantity += quantity;
        UpdatedAtUtc = confirmedAtUtc;
        return result.Value!;
    }

    public OperationResult CompleteSource(
        Guid cycleId, Guid scannedSourceId, bool sourceWasEmpty,
        string userId, DateTimeOffset completedAtUtc)
    {
        if (Status != ReplenishmentTaskStatus.InProgress || AssignedTo != userId)
            return OperationError.Conflict("Завершить работу с поддоном может исполнитель задания.");
        if (completedAtUtc == default || completedAtUtc < UpdatedAtUtc)
            return OperationError.Invalid("Время завершения предшествует последнему действию задания.");
        var cycle = _sourceCycles.SingleOrDefault(x => x.Id == cycleId);
        if (cycle is null)
            return OperationError.NotFound("Цикл источника Подпитки не найден.");

        var result = cycle.Complete(scannedSourceId, sourceWasEmpty, completedAtUtc);
        if (!result.IsSuccess)
            return result;
        UpdatedAtUtc = completedAtUtc;
        if (RemainingQuantity == 0)
        {
            Status = ReplenishmentTaskStatus.Completed;
            CompletedAtUtc = completedAtUtc;
        }
        return OperationResult.Success();
    }

    public OperationResult Cancel(string userId, DateTimeOffset cancelledAtUtc)
    {
        if (Status != ReplenishmentTaskStatus.Ordered || _sourceCycles.Count != 0)
            return OperationError.Conflict("Отменить можно только ещё не начатую Подпитку.");
        if (string.IsNullOrWhiteSpace(userId) || userId.Length > 36
            || cancelledAtUtc == default || cancelledAtUtc < UpdatedAtUtc)
            return OperationError.Invalid("Для отмены обязательны пользователь и корректное время.");

        Status = ReplenishmentTaskStatus.Cancelled;
        CancelledBy = userId.Trim();
        CancelledAtUtc = cancelledAtUtc;
        UpdatedAtUtc = cancelledAtUtc;
        return OperationResult.Success();
    }
}
