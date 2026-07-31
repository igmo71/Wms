using System.Text.Json.Serialization;

namespace Wms.Integration.OneS.Models;

internal class Document_ПриходныйОрдерНаТовары
{
    public Guid Ref_Key { get; set; }
    public string? DataVersion { get; set; }
    public bool DeletionMark { get; set; }
    public string? Number { get; set; }
    public DateTime Date { get; set; }
    public bool Posted { get; set; }
    public Guid? Склад_Key { get; set; }
    public string? Комментарий { get; set; }
    public string? Статус { get; set; }
    public string? СкладскаяОперация { get; set; }
    public string? ХозяйственнаяОперация { get; set; }

    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int ВсегоМест { get; set; }

    public Guid? Отправитель { get; set; }
    public string? Отправитель_Type { get; set; }
    public Guid? Распоряжение { get; set; }
    public string? Распоряжение_Type { get; set; }
    public string? Доброга_ТипОчереди { get; set; }
    public List<Document_ПриходныйОрдерНаТовары_Товары> Товары { get; set; } = [];


    public const int BatchSize = 10;

    public static string TotalUri => "Document_ПриходныйОрдерНаТовары/$count?$format=json";

    private static readonly string select =
        "Ref_Key,DataVersion,DeletionMark,Number,Date,Posted,Склад_Key,Комментарий,Статус,СкладскаяОперация,ВсегоМест," +
        "Отправитель,Отправитель_Type,Распоряжение,Распоряжение_Type,ХозяйственнаяОперация,Доброга_ТипОчереди,Товары";


    public static string GetListUri(DateTime dateFrom, DateTime dateTo, int page) => $"Document_ПриходныйОрдерНаТовары" +
        $"?$format=json" +
        $"&$select={select}" +
        $"&$filter=Date ge datetime'{dateFrom:s}' and Date lt datetime'{dateTo:s}'" +
        $"&$orderby=Date" +
        $"&$skip={page * BatchSize}" +
        $"&$top={BatchSize}";

    public static string GetUri(string refKey) => $"Document_ПриходныйОрдерНаТовары" +
        $"?$format=json" +
        $"&$select={select}" +
        $"&$filter=Ref_Key eq guid'{refKey}'";


    public static string PatchUri(string refKey) => $"Document_ПриходныйОрдерНаТовары(guid'{refKey}')?$format=json";

    public static string PostDocumentUri(string refKey) => $"Document_ПриходныйОрдерНаТовары(guid'{refKey}')/Post?$format=json";
}

internal class Document_ПриходныйОрдерНаТовары_Товары
{
    public Guid Ref_Key { get; set; }

    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int LineNumber { get; set; }

    public Guid? Номенклатура_Key { get; set; }
    public double КоличествоУпаковок { get; set; }
    public double Количество { get; set; }
    public string? Штрихкод { get; set; }
    public string? Комментарий { get; set; }
}



