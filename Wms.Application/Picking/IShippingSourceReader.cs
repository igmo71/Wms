using Wms.Common;
using Wms.Domain.Enums;

namespace Wms.Application.Picking;

public interface IShippingSourceReader
{
    Task<OperationResult<ShippingSourceSnapshot>> ReadAsync(Guid refKey, CancellationToken ct);
}

public interface IShippingStoredSnapshotParser
{
    OperationResult<ShippingSourceSnapshot> Parse(Guid refKey, string storedDocumentJson);
}

public sealed record ShippingSourceSnapshot(
    Guid RefKey,
    string RawDocumentJson,
    bool? Posted,
    bool? DeletionMark,
    string? Number,
    DateTime? Date,
    Guid? WarehouseId,
    string? Comment,
    string? RawStatus,
    ShippingOrderStatus Status,
    string? RawWarehouseOperation,
    WarehouseOperation WarehouseOperation,
    Guid? ReceiverId,
    string? RawReceiverType,
    PartyType? ReceiverType,
    string? RawQueue,
    ShippingOrderQueue Queue,
    DateTime? PlannedShippingDate,
    Guid? DeliveryDirectionId,
    IReadOnlyList<ShippingSourceItem> Items,
    IReadOnlyList<ShippingSourceBasisItem> BasisItems);

public sealed record ShippingSourceItem(
    int LineNumber, Guid? StockKeepingUnitId,
    string? RawQuantity, decimal? Quantity,
    string? RawPackageQuantity, decimal? PackageQuantity,
    string? RawAction, ShippingOrderAction Action,
    bool? IsPackagingList, bool? IsServiceLine,
    string RawLineJson)
{
    public bool IsRegular => IsPackagingList == false && IsServiceLine == false;
}

public sealed record ShippingSourceBasisItem(
    int LineNumber, Guid? StockKeepingUnitId,
    string? RawQuantity, decimal? Quantity,
    Guid? BasisOrderId, string? BasisOrderType,
    string RawLineJson);
