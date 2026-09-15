namespace Wms.Application.LicensePlateNumbers;

public sealed record OpenReceivingLicensePlateNumberCommand(Guid ReceivingOrderId, string Code);
public sealed record CancelReceivingLicensePlateNumberCommand(Guid ReceivingOrderId, Guid LicensePlateNumberId);
