namespace Wms.Application.LicensePlateNumbers;

public sealed record OpenReceivingLicensePlateNumberCommand(Guid ReceivingOrderId, string Code);
public sealed record CancelReceivingLicensePlateNumberCommand(Guid ReceivingOrderId, Guid LicensePlateNumberId);
public sealed record SetReceivingLicensePlateNumberItemQuantityCommand(
    Guid ReceivingOrderId,
    Guid LicensePlateNumberId,
    Guid StockKeepingUnitId,
    decimal Quantity);
public sealed record RemoveReceivingLicensePlateNumberItemCommand(
    Guid ReceivingOrderId,
    Guid LicensePlateNumberId,
    Guid StockKeepingUnitId);
public sealed record CloseReceivingLicensePlateNumberCommand(Guid ReceivingOrderId, Guid LicensePlateNumberId);
public sealed record ReopenReceivingLicensePlateNumberCommand(Guid ReceivingOrderId, Guid LicensePlateNumberId);
