using Wms.Domain;

namespace Wms.Application.Services.Transfers;

public class TransferOrderTransitBalance
{
    public StockKeepingUnit StockKeepingUnit { get; init; } = null!;
    public double Quantity { get; init; }
}
