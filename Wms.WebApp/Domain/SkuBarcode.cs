using Wms.WebApp.Abstractions;

namespace Wms.WebApp.Domain;

public class SkuBarcode : EntityBase
{
    public required string Value { get; set; }

    public Guid SkuId { get; set; }
    public StockKeepingUnit? Sku { get; set; }
}
