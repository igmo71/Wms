using System.Text.Json.Serialization;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Integration.OneS.Models;

internal class Document_ПриходныйОрдерНаТовары
{
    public Guid Ref_Key { get; set; }
    public bool DeletionMark { get; set; }
    public bool Posted { get; set; }
    public string? Number { get; set; }
    public DateTime Date { get; set; }
    public Guid Склад_Key { get; set; }
    public string? Статус { get; set; }
    public string? СкладскаяОперация { get; set; }
    public Guid Отправитель { get; set; }
    public string? Отправитель_Type { get; set; }
    public string? Комментарий { get; set; }
    public string? Доброга_ТипОчереди { get; set; }
    public Guid Распоряжение { get; set; }
    public string? Распоряжение_Type { get; set; }
    public string? ХозяйственнаяОперация { get; set; }

    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int ВсегоМест { get; set; }
    public List<Document_ПриходныйОрдерНаТовары_Товары> Товары { get; set; } = [];


    public const int BatchSize = 10;

    public static string TotalUri => "Document_ПриходныйОрдерНаТовары/$count?$format=json";

    private static readonly string select =
        "Ref_Key,DeletionMark,Posted,Number,Date,Склад_Key,Статус,СкладскаяОперация,Отправитель,Отправитель_Type,Комментарий," +
        "Доброга_ТипОчереди,Распоряжение,Распоряжение_Type,ХозяйственнаяОперация,ВсегоМест,Товары";

    public static string GetListUri(DateTime dateFrom, DateTime dateTo, int page) =>
        $"Document_ПриходныйОрдерНаТовары" +
        $"?$format=json" +
        $"&$select={select}" +
        $"&$filter=Date ge datetime'{dateFrom:s}' and Date lt datetime'{dateTo:s}'" +
        $"&$orderby=Date" +
        $"&$skip={page * BatchSize}" +
        $"&$top={BatchSize}";

    public static string GetUri(string refKey) =>
        $"Document_ПриходныйОрдерНаТовары" +
        $"?$format=json" +
        $"&$select={select}" +
        $"&$filter=Ref_Key eq guid'{refKey}'";

    public static string PatchUri(string refKey) => $"Document_ПриходныйОрдерНаТовары(guid'{refKey}')?$format=json";

    public static string PostDocumentUri(string refKey) => $"Document_ПриходныйОрдерНаТовары(guid'{refKey}')/Post?$format=json";

    public static ReceivingOrderImportSnapshot MapToImportSnapshot(
        Document_ПриходныйОрдерНаТовары fetchedDocument)
    {
        var items = fetchedDocument.Товары
            .Select(x => new ReceivingOrderItemImportSnapshot(
                x.LineNumber,
                x.Номенклатура_Key,
                x.КоличествоУпаковок)) // В текущем внедрении упаковки не используются, количества в 1С совпадают 1:1.
            .ToList();

        return new ReceivingOrderImportSnapshot(
            fetchedDocument.Ref_Key,
            fetchedDocument.DeletionMark,
            fetchedDocument.Posted,
            fetchedDocument.Number,
            fetchedDocument.Date,
            fetchedDocument.Склад_Key,
            fetchedDocument.Комментарий,
            ODataEnumMapper.Parse<ReceivingOrderStatus>(fetchedDocument.Статус),
            ODataEnumMapper.Parse<ReceivingOrderQueue>(fetchedDocument.Доброга_ТипОчереди),
            ODataEnumMapper.Parse<WarehouseOperation>(fetchedDocument.СкладскаяОперация),
            ODataEnumMapper.Parse<BusinessOperation>(fetchedDocument.ХозяйственнаяОперация),
            fetchedDocument.Отправитель,
            ODataEnumMapper.Parse<PartyType>(fetchedDocument.Отправитель_Type),
            fetchedDocument.Распоряжение,
            fetchedDocument.Распоряжение_Type.TrimODataPrefix(),
            items);
    }
}




