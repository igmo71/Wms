namespace Wms.Domain;

public class SkuBarcode
{
    public Guid SkuId { get; set; }
    public StockKeepingUnit? Sku { get; set; }

    public required string Value { get; set; }
}
