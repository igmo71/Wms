using Wms.Application.CommandExecution;
using Wms.Common;
using Wms.Domain;

namespace Wms.Application.Inventory.Counts;

public sealed record InventoryCountBalance(Guid SkuId, DateOnly? ExpirationDate, decimal Quantity);

public sealed record InventoryCountStockCommitments(
    bool HasRouteReservation, bool HasConfirmedPickReservation, bool HasOpenShortage);

public interface IInventoryCountStore
{
    Task<Guid?> FindLocationIdAsync(ICommandExecutionContext session,
        Guid warehouseId, string code, CancellationToken ct);
    Task<StorageLocation?> LockLocationAsync(ICommandExecutionContext session,
        Guid locationId, CancellationToken ct);
    Task<(InventoryCount Count, StorageLocation Location)?> LoadDraftAsync(
        ICommandExecutionContext session, Guid countId, CancellationToken ct);
    Task<IReadOnlyList<InventoryCountBalance>> LoadBalancesAsync(ICommandExecutionContext session,
        Guid warehouseId, Guid locationId, CancellationToken ct);
    Task<InventoryCountStockCommitments> GetStockCommitmentsAsync(
        ICommandExecutionContext session, Guid locationId, CancellationToken ct);
    Task<OperationResult<StockKeepingUnit>> ResolveSkuAsync(ICommandExecutionContext session,
        string? barcode, CancellationToken ct);
    Task<StockKeepingUnit?> LoadSkuAsync(ICommandExecutionContext session, Guid skuId, CancellationToken ct);
    void Remove(ICommandExecutionContext session, object entity);
}
