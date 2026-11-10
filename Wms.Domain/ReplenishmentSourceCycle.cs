using Wms.Common;

namespace Wms.Domain;

public sealed class ReplenishmentSourceCycle
{
    private readonly List<ReplenishmentTransfer> _transfers = [];
    private ReplenishmentSourceCycle() { }

    public Guid Id { get; private set; }
    public Guid ReplenishmentTaskId { get; private set; }
    public Guid SourceStorageLocationId { get; private set; }
    public string StartedBy { get; private set; } = null!;
    public DateTimeOffset StartedAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public bool? SourceWasEmpty { get; private set; }
    public IReadOnlyCollection<ReplenishmentTransfer> Transfers => _transfers;

    internal static OperationResult<ReplenishmentSourceCycle> Create(
        Guid id, Guid taskId, Guid sourceId, string userId, DateTimeOffset startedAtUtc)
    {
        if (id == Guid.Empty || taskId == Guid.Empty || sourceId == Guid.Empty)
            return OperationError.Invalid("Идентификаторы источника Подпитки обязательны.");
        if (string.IsNullOrWhiteSpace(userId) || userId.Length > 36 || startedAtUtc == default)
            return OperationError.Invalid("Для источника Подпитки обязательны пользователь и время.");

        return new ReplenishmentSourceCycle
        {
            Id = id,
            ReplenishmentTaskId = taskId,
            SourceStorageLocationId = sourceId,
            StartedBy = userId.Trim(),
            StartedAtUtc = startedAtUtc
        };
    }

    internal OperationResult AddTransfer(ReplenishmentTransfer transfer)
    {
        if (CompletedAtUtc is not null)
            return OperationError.Conflict("Работа с исходным поддоном уже завершена.");
        if (transfer.ReplenishmentSourceCycleId != Id || transfer.ConfirmedBy != StartedBy
            || transfer.DestinationStorageLocationId == SourceStorageLocationId
            || transfer.ConfirmedAtUtc < StartedAtUtc)
            return OperationError.Invalid("Шаг Подпитки не соответствует снятому поддону.");
        if (_transfers.Any(x => x.Id == transfer.Id || x.InventoryMovementId == transfer.InventoryMovementId))
            return OperationError.Conflict("Шаг Подпитки уже сохранён.");

        _transfers.Add(transfer);
        return OperationResult.Success();
    }

    internal OperationResult Complete(
        Guid scannedSourceId, bool sourceWasEmpty, DateTimeOffset completedAtUtc)
    {
        if (CompletedAtUtc is not null)
            return OperationError.Conflict("Работа с исходным поддоном уже завершена.");
        if (scannedSourceId != SourceStorageLocationId)
            return OperationError.Invalid("Отсканируйте исходную ячейку снятого поддона.");
        if (completedAtUtc == default || completedAtUtc < StartedAtUtc
            || _transfers.Any(x => x.ConfirmedAtUtc > completedAtUtc))
            return OperationError.Invalid("Время возврата поддона предшествует выполненным действиям.");

        CompletedAtUtc = completedAtUtc;
        SourceWasEmpty = sourceWasEmpty;
        return OperationResult.Success();
    }
}
