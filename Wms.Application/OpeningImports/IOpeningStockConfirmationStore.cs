using Wms.Application.CommandExecution;
using Wms.Common;
using Wms.Domain;

namespace Wms.Application.OpeningImports;

public sealed record OpeningStockConfirmationState(
    OpeningStockImportState Snapshot,
    WarehouseOpeningImport? Import,
    IReadOnlyDictionary<Guid, StockKeepingUnit> Skus);

public interface IOpeningStockConfirmationStore
{
    Task<OperationResult<OpeningStockConfirmationState>> LoadAsync(
        ICommandExecutionContext session, Guid warehouseId,
        IReadOnlyCollection<Guid> skuIds, CancellationToken ct);
}

public readonly record struct OpeningBalanceKey(
    Guid WarehouseId, Guid StorageLocationId,
    Guid StockKeepingUnitId, DateOnly? ExpirationDate);

public sealed record OpeningInventoryPostingState(
    IReadOnlyDictionary<Guid, StorageLocation> Locations,
    Dictionary<OpeningBalanceKey, InventoryBalance> Balances);

public interface IOpeningInventoryPostingStore
{
    Task<OpeningInventoryPostingState> LoadOpeningAsync(
        ICommandExecutionContext session, Guid warehouseId,
        IReadOnlyCollection<Guid> locationIds, CancellationToken ct);
}
