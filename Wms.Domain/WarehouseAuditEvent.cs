using System.Text.Json;
using Wms.Common;

namespace Wms.Domain;

public sealed class WarehouseAuditEvent
{
    private WarehouseAuditEvent() { }

    public Guid Id { get; private set; }
    public string EventType { get; private set; } = null!;
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public string? ActorUserId { get; private set; }
    public string? ActionSource { get; private set; }
    public Guid? ReceivingOrderId { get; private set; }
    public Guid? LicensePlateNumberId { get; private set; }
    public Guid? StockKeepingUnitId { get; private set; }
    public Guid? ReceivingSourceDocumentId { get; private set; }
    public string? BeforeValuesJson { get; private set; }
    public string? AfterValuesJson { get; private set; }
    public string MovementIdsJson { get; private set; } = "[]";
    public string SourceVersionIdsJson { get; private set; } = "[]";
    public string? CommandUserId { get; private set; }
    public string? CommandType { get; private set; }
    public Guid? RequestId { get; private set; }

    public static OperationResult<WarehouseAuditEvent> Create(
        string eventType,
        DateTimeOffset occurredAtUtc,
        string? actorUserId,
        string? actionSource,
        Guid? receivingOrderId,
        Guid? licensePlateNumberId,
        Guid? stockKeepingUnitId,
        Guid? receivingSourceDocumentId,
        string? beforeValuesJson,
        string? afterValuesJson,
        IReadOnlyCollection<Guid>? movementIds = null,
        IReadOnlyCollection<Guid>? sourceVersionIds = null,
        string? commandUserId = null,
        string? commandType = null,
        Guid? requestId = null)
    {
        if (string.IsNullOrWhiteSpace(eventType) || eventType.Length > 100
            || occurredAtUtc == default)
            return OperationError.Invalid("Тип и время события обязательны.");

        if (string.IsNullOrWhiteSpace(actorUserId) == string.IsNullOrWhiteSpace(actionSource)
            || actorUserId?.Length > 36 || actionSource?.Length > 100)
            return OperationError.Invalid("Укажите исполнителя или источник события.");

        Guid?[] affectedIds = [receivingOrderId, licensePlateNumberId,
            stockKeepingUnitId, receivingSourceDocumentId];
        if (affectedIds.All(x => x is null) || affectedIds.Any(x => x == Guid.Empty))
            return OperationError.Invalid("Событие должно ссылаться на затронутый объект.");

        if (!IsCompactObject(beforeValuesJson) || !IsCompactObject(afterValuesJson))
            return OperationError.Invalid("Измененные значения должны быть компактными JSON-объектами.");

        var movements = movementIds?.ToArray() ?? [];
        var versions = sourceVersionIds?.ToArray() ?? [];
        if (!HasDistinctIds(movements) || !HasDistinctIds(versions))
            return OperationError.Invalid("Ссылки события должны содержать неповторяющиеся идентификаторы.");

        var hasCommandPart = commandUserId is not null || commandType is not null || requestId is not null;
        if (hasCommandPart && (string.IsNullOrWhiteSpace(commandUserId)
            || string.IsNullOrWhiteSpace(commandType)
            || requestId is null || requestId == Guid.Empty
            || commandUserId is { Length: > 36 }
            || commandType is { Length: > 100 }))
            return OperationError.Invalid("Ссылка на команду должна содержать полный ключ квитанции.");

        return new WarehouseAuditEvent
        {
            Id = Guid.NewGuid(),
            EventType = eventType.Trim(),
            OccurredAtUtc = occurredAtUtc,
            ActorUserId = actorUserId?.Trim(),
            ActionSource = actionSource?.Trim(),
            ReceivingOrderId = receivingOrderId,
            LicensePlateNumberId = licensePlateNumberId,
            StockKeepingUnitId = stockKeepingUnitId,
            ReceivingSourceDocumentId = receivingSourceDocumentId,
            BeforeValuesJson = beforeValuesJson,
            AfterValuesJson = afterValuesJson,
            MovementIdsJson = JsonSerializer.Serialize(movements),
            SourceVersionIdsJson = JsonSerializer.Serialize(versions),
            CommandUserId = commandUserId?.Trim(),
            CommandType = commandType?.Trim(),
            RequestId = requestId
        };
    }

    private static bool HasDistinctIds(Guid[] ids) =>
        ids.All(x => x != Guid.Empty) && ids.Distinct().Count() == ids.Length;

    private static bool IsCompactObject(string? json)
    {
        if (json is null)
            return true;
        if (json.Length is 0 or > 4000)
            return false;

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
