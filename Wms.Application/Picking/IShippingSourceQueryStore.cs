using Wms.Domain.Enums;

namespace Wms.Application.Picking;

public sealed record ShippingSourceLineView(int LineNumber, Guid SkuId,
    string? SkuCode, string? SkuName, decimal Quantity);

public sealed record ShippingSourceListItem(Guid Id, string? Number, DateTime Date,
    ShippingOrderStatus Status, bool Posted, bool DeletionMark,
    Guid WarehouseId, string? WarehouseName, string? ReceiverName,
    DateTimeOffset? LastCheckedAtUtc, string? LastCheckError,
    Guid? AssemblyId, string? AssemblyNumber,
    IReadOnlyList<ShippingSourceLineView> Lines);

public interface IShippingSourceQueryStore
{
    Task<IReadOnlyList<ShippingSourceListItem>> ListAsync(CancellationToken ct);
}
