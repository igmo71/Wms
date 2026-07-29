namespace Wms.Domain;

public class SkuBarcode
{
    public Guid SkuId { get; set; }
    public StockKeepingUnit? Sku { get; set; }

    public string? Value { get; set; }
}
