using System.Text.Json.Serialization;

namespace Wms.Integration.OneS.Models;

internal class Document_РасходныйОрдерНаТовары_ОтгружаемыеТовары
{
    public Guid Ref_Key { get; set; }

    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int LineNumber { get; set; }

    public Guid Номенклатура_Key { get; set; }
    public decimal Количество { get; set; }
    public decimal КоличествоУпаковок { get; set; }
    public string? Действие { get; set; }
    public Guid? Характеристика_Key { get; set; }
    public Guid? Назначение_Key { get; set; }
    public Guid? Серия_Key { get; set; }
    public int СтатусУказанияСерий { get; set; }
    public bool ЭтоУпаковочныйЛист { get; set; }
    public Guid? Упаковка_Key { get; set; }
    public Guid? УпаковочныйЛист_Key { get; set; }
    public Guid? УпаковочныйЛистРодитель_Key { get; set; }
    public int ЭтоСлужебнаяСтрокаПустогоУпаковочногоЛиста { get; set; }
}
