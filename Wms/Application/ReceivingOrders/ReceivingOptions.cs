using Wms.Domain.Enums;

namespace Wms.Application.ReceivingOrders;

public sealed class ReceivingOptions
{
    public ReceivingIntegrationMode ReceivingIntegrationMode { get; set; } = ReceivingIntegrationMode.Connected;
}
