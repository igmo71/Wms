namespace Wms.Contracts.Mobile.V1;

public sealed record MobileStockCorrectionCellResponse(Guid WarehouseId, Guid LocationId,
    string Code, string Address, string Name, string? HoldReason,
    IReadOnlyList<MobileStockCorrectionGroup> Groups);

public sealed record MobileStockCorrectionGroup(Guid SkuId, string SkuCode, string SkuName, string? Unit,
    DateOnly? ExpirationDate, decimal AccountingQuantity, decimal ConfirmedPickQuantity,
    decimal RouteReservedQuantity, decimal ShortageBlockedQuantity, decimal AvailableQuantity,
    bool CanAdd);

public sealed record MobileCorrectCellStockRequest(Guid ClientRequestId, Guid WarehouseId,
    Guid LocationId, Guid SkuId, DateOnly? ExpirationDate, decimal QuantityChange);

public sealed record MobileCorrectCellStockResponse(Guid MovementId);
