using System.Text.Json.Serialization;
using Wms.Domain;

namespace Wms.Integration.OneS.Models;

internal class Document_ПриходныйОрдерНаТовары_Товары
{
    public Guid Ref_Key { get; set; }

    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int LineNumber { get; set; }

    public Guid Номенклатура_Key { get; set; }
    public decimal КоличествоУпаковок { get; set; }
    public decimal Количество { get; set; }
    public string? Штрихкод { get; set; }
    public string? Комментарий { get; set; }

    public static Document_ПриходныйОрдерНаТовары_Товары MapFromReceivingOrderItem(ReceivingOrderItem item)
    {
        return new Document_ПриходныйОрдерНаТовары_Товары
        {
            Ref_Key = item.ReceivingOrderId,
            LineNumber = item.LineNumber,
            Количество = item.FactQuantity,
            КоличествоУпаковок = item.FactQuantity,
            Номенклатура_Key = item.StockKeepingUnitId,
            Комментарий = item.Comment
        };
    }
}
