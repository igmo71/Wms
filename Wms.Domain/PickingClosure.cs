using System.Text.Json;
using Wms.Common;

namespace Wms.Domain;

public sealed class PickingClosure
{
    private PickingClosure() { }

    public Guid PickingAssemblyId { get; private set; }
    public string ClosedBy { get; private set; } = null!;
    public DateTimeOffset ClosedAtUtc { get; private set; }
    public bool ManagerOverride { get; private set; }
    public string? DecisionReason { get; private set; }
    public string ReviewedSourcesJson { get; private set; } = null!;
    public string MovementIdsJson { get; private set; } = null!;

    public static OperationResult<PickingClosure> Create(Guid assemblyId,
        IReadOnlyCollection<PickingEntry> entries,
        IReadOnlyCollection<PickingAllocation> allocations,
        IReadOnlyCollection<Guid> movementIds, string reviewedSourcesJson,
        string userId, DateTimeOffset closedAtUtc,
        bool managerOverride = false, string? decisionReason = null)
    {
        if (assemblyId == Guid.Empty || entries is null || allocations is null
            || movementIds is null || movementIds.Any(x => x == Guid.Empty)
            || string.IsNullOrWhiteSpace(userId) || userId.Trim().Length > 36
            || closedAtUtc == default || !IsJsonObject(reviewedSourcesJson)
            || decisionReason?.Length > 2000)
            return OperationError.Invalid("Результат закрытия содержит неполные данные.");
        if (entries.Any(x => x.PickingAssemblyId != assemblyId)
            || allocations.Any(x => x.PickingAssemblyId != assemblyId))
            return OperationError.Invalid("Факт и распределение относятся к разным сборкам.");
        var fact = entries.Where(x => x.Quantity > 0)
            .GroupBy(x => x.StockKeepingUnitId)
            .ToDictionary(x => x.Key, x => x.Sum(row => row.Quantity));
        var assigned = allocations.GroupBy(x => x.StockKeepingUnitId)
            .ToDictionary(x => x.Key, x => x.Sum(row => row.Quantity));
        if (fact.Any(x => assigned.GetValueOrDefault(x.Key) != x.Value)
            || assigned.Any(x => fact.GetValueOrDefault(x.Key) != x.Value))
            return OperationError.Conflict("Распределение по РО 1С должно совпадать с фактом WMS для каждого SKU.");
        if (fact.Count == 0 && movementIds.Count != 0)
            return OperationError.Conflict("Нулевой факт не создаёт складских движений.");
        if (fact.Count != 0 && movementIds.Count == 0)
            return OperationError.Conflict("Для положительного факта нужны складские движения.");
        if (movementIds.Distinct().Count() != movementIds.Count)
            return OperationError.Invalid("Складское движение указано повторно.");

        return new PickingClosure
        {
            PickingAssemblyId = assemblyId, ClosedBy = userId.Trim(),
            ClosedAtUtc = closedAtUtc, ManagerOverride = managerOverride,
            DecisionReason = decisionReason?.Trim(),
            ReviewedSourcesJson = reviewedSourcesJson,
            MovementIdsJson = JsonSerializer.Serialize(movementIds)
        };
    }

    private static bool IsJsonObject(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            using var parsed = JsonDocument.Parse(json);
            return parsed.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException) { return false; }
    }
}
