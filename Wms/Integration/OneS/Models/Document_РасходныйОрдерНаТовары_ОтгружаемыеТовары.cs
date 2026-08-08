using System.Text.Json.Serialization;

namespace Wms.Integration.OneS.Models;

internal class Document_РасходныйОрдерНаТовары_ОтгружаемыеТовары
{
    public Guid Ref_Key { get; set; }

    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int LineNumber { get; set; }

    public Guid Номенклатура_Key { get; set; }
    public double Количество { get; set; }
    public double КоличествоУпаковок { get; set; }
    public string? Действие { get; set; }

    //public Guid? Характеристика_Key { get; set; }
}
