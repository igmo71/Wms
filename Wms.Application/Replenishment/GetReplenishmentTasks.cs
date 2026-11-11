using Wms.Application.Authorization;
using Wms.Common;
using Wms.Domain.Enums;

namespace Wms.Application.Replenishment;

public sealed record ReplenishmentTaskListItem(
    Guid Id, Guid PickingAssemblyId, string AssemblyNumber,
    Guid StockKeepingUnitId, string? SkuCode, string? SkuName,
    decimal OrderedQuantity, decimal TransferredQuantity,
    ReplenishmentTaskStatus Status, string? AssignedTo,
    DateTimeOffset OrderedAtUtc, ShippingOrderQueue PriorityQueue,
    Guid? RecommendedSourceLocationId, string? RecommendedSourceCode,
    Guid? RecommendedDestinationLocationId, string? RecommendedDestinationCode)
{
    public decimal RemainingQuantity => OrderedQuantity - TransferredQuantity;
}

public sealed class GetReplenishmentTasks(
    IReplenishmentStore store, WarehousePermission permission)
{
    public async Task<OperationResult<IReadOnlyList<ReplenishmentTaskListItem>>> ExecuteAsync(
        Guid warehouseId, string userId, CancellationToken ct = default)
    {
        if (warehouseId == Guid.Empty || string.IsNullOrWhiteSpace(userId))
            return OperationError.Invalid("Выберите склад и сотрудника Подпитки.");
        var access = await permission.RequireOperatorAsync(userId, ct);
        if (!access.IsSuccess) return access.Error!;

        var entries = await store.ReadQueueAsync(warehouseId, ct);
        var ordered = entries.Select(x =>
            new ReplenishmentTaskListItem(x.Id, x.PickingAssemblyId,
                x.AssemblyNumber, x.StockKeepingUnitId, x.SkuCode, x.SkuName,
                x.OrderedQuantity, x.TransferredQuantity, x.Status, x.AssignedTo,
                x.OrderedAtUtc, x.SourceQueues.OrderBy(Rank).FirstOrDefault(),
                x.RecommendedSourceLocationId, x.RecommendedSourceCode,
                x.RecommendedDestinationLocationId,
                x.RecommendedDestinationCode))
            .OrderBy(x => Rank(x.PriorityQueue))
            .ThenBy(x => x.OrderedAtUtc)
            .ThenBy(x => x.Id)
            .ToArray();
        return OperationResult<IReadOnlyList<ReplenishmentTaskListItem>>.Success(ordered);
    }

    private static int Rank(ShippingOrderQueue queue) => queue switch
    {
        ShippingOrderQueue.LiveQueue => 0,
        ShippingOrderQueue.CollectByDate => 1,
        ShippingOrderQueue.OwnDelivery => 2,
        _ => 3
    };
}
