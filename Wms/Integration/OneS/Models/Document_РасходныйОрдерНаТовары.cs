using System.Text.Json.Serialization;

namespace Wms.Integration.OneS.Models;

internal class Document_РасходныйОрдерНаТовары
{
    public Guid Ref_Key { get; set; }
    public bool DeletionMark { get; set; }
    public bool Posted { get; set; }
    public string? Number { get; set; }
    public DateTime Date { get; set; }
    public Guid? Склад_Key { get; set; }
    public string? Статус { get; set; }
    public string? СкладскаяОперация { get; set; }
    public Guid Получатель { get; set; }
    public string? Получатель_Type { get; set; }
    public string? Комментарий { get; set; }
    public string? Доброга_ТипОчереди { get; set; }
    public string? Доброга_НаправлениеДоставки_Key { get; set; }
    public DateTime Доброга_ПланируемаяДатаОтгрузки { get; set; }

    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int ВсегоМест { get; set; }
    public List<Document_РасходныйОрдерНаТовары_ТоварыПоРаспоряжениям>? ТоварыПоРаспоряжениям { get; set; }
    public List<Document_РасходныйОрдерНаТовары_ОтгружаемыеТовары>? ОтгружаемыеТовары { get; set; }

    public const int BatchSize = 10;

    public static string TotalUri => "Document_РасходныйОрдерНаТовары/$count?$format=json";

    private static readonly string select =
        "Ref_Key,DeletionMark,Posted,Number,Date,Склад_Key,Статус,СкладскаяОперация,Получатель,Получатель_Type,Комментарий," +
        "Доброга_ТипОчереди,Доброга_НаправлениеДоставки_Key,Доброга_ПланируемаяДатаОтгрузки,ВсегоМест,ТоварыПоРаспоряжениям,ОтгружаемыеТовары";

    public static string GetListUri(DateTime dateFrom, DateTime dateTo, int page) =>
        $"Document_РасходныйОрдерНаТовары" +
        $"?$format=json" +
        $"&$select={select}" +
        $"&$filter=Date ge datetime'{dateFrom:s}' and Date lt datetime'{dateTo:s}'" +
        $"&$orderby=Date" +
        $"&$skip={page * BatchSize}" +
        $"&$top={BatchSize}";

    public static string GetUri(string refKey) =>
        $"Document_РасходныйОрдерНаТовары" +
        $"?$format=json" +
        $"&$select={select}" +
        $"&$filter=Ref_Key eq guid'{refKey}'";

    public static string PatchUri(string refKey) => $"Document_РасходныйОрдерНаТовары(guid'{refKey}')?$format=json";

    public static string PostDocumentUri(string refKey) => $"Document_РасходныйОрдерНаТовары(guid'{refKey}')/Post?$format=json";
}
