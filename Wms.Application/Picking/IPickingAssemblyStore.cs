using Wms.Application.CommandExecution;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.Picking;

public sealed record PickingAssemblyChangeState(
    ShippingOrder? Source, PickingAssembly? Assembly, Guid? LinkedAssemblyId);

public interface IPickingAssemblyStore
{
    Task<Guid?> FindLinkedAssemblyIdAsync(Guid sourceId, CancellationToken ct);
    Task<PickingAssembly?> ReadAssemblyAsync(Guid assemblyId, CancellationToken ct);
    Task<ShippingOrder?> ReadSourceAsync(Guid sourceId, CancellationToken ct);
    Task<PickingAssembly?> LoadAssemblyAsync(
        ICommandExecutionContext session, Guid assemblyId, CancellationToken ct);
    Task<PickingAssemblyChangeState> LoadAsync(
        ICommandExecutionContext session, Guid? assemblyId, Guid sourceId, CancellationToken ct);
    Task<OperationResult> AssignNumberAsync(
        ICommandExecutionContext session, PickingAssembly assembly, CancellationToken ct);
}

internal static class PickingSourceEligibility
{
    public static OperationResult<IReadOnlyDictionary<Guid, decimal>> Plan(
        ShippingOrder source, Guid? expectedWarehouseId = null)
    {
        if (source.CurrentSourceJson is null || source.LastSourceCheckError is not null)
            return OperationError.Conflict("Документ 1С ещё не прошёл успешную сверку.");
        if (source.Status != ShippingOrderStatus.Prepared || !source.Posted
            || source.DeletionMark)
            return OperationError.Conflict("Для сборки нужен проведённый РО 1С в статусе Подготовлен.");
        if (expectedWarehouseId is Guid warehouseId && source.WarehouseId != warehouseId)
            return OperationError.Conflict("Документ 1С относится к другому складу.");

        var result = new Dictionary<Guid, decimal>();
        foreach (var item in source.Items)
        {
            if (!WarehouseQuantity.IsNonNegative(item.PlanQuantity))
                return OperationError.Conflict("Количество источника не поддерживается.");
            var quantity = result.GetValueOrDefault(item.StockKeepingUnitId) + item.PlanQuantity;
            if (!WarehouseQuantity.IsNonNegative(quantity))
                return OperationError.Conflict("Количество SKU в документе превышает допустимое.");
            result[item.StockKeepingUnitId] = quantity;
        }
        return OperationResult<IReadOnlyDictionary<Guid, decimal>>.Success(result);
    }
}
