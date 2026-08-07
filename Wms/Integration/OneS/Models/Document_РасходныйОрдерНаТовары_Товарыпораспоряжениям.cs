using System.Text.Json.Serialization;

namespace Wms.Integration.OneS.Models;

internal class Document_РасходныйОрдерНаТовары_Товарыпораспоряжениям
{
    public Guid Ref_Key { get; set; }

    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int LineNumber { get; set; }

    public Guid Номенклатура_Key { get; set; }
    public double Количество { get; set; }
    public Guid? Распоряжение { get; set; }
    public string? Распоряжение_Type { get; set; }

    //public Guid? Характеристика_Key { get; set; }
}
