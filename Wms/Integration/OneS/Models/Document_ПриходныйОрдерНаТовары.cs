using System.Text.Json.Serialization;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Integration.OneS.Models;

internal class Document_ПриходныйОрдерНаТовары
{
    public Guid Ref_Key { get; set; }
    public bool DeletionMark { get; set; }
    public string? Number { get; set; }
    public DateTime Date { get; set; }
    public bool Posted { get; set; }
    public Guid Склад_Key { get; set; }
    public string? Комментарий { get; set; }
    public string? Статус { get; set; }
    public string? СкладскаяОперация { get; set; }
    public string? ХозяйственнаяОперация { get; set; }

    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int ВсегоМест { get; set; }

    public Guid Отправитель { get; set; }
    public string? Отправитель_Type { get; set; }
    public Guid Распоряжение { get; set; }
    public string? Распоряжение_Type { get; set; }
    public string? Доброга_ТипОчереди { get; set; }
    public List<Document_ПриходныйОрдерНаТовары_Товары> Товары { get; set; } = [];


    public const int BatchSize = 10;

    public static string TotalUri => "Document_ПриходныйОрдерНаТовары/$count?$format=json";

    private static readonly string select =
        "Ref_Key,DeletionMark,Number,Date,Posted,Склад_Key,Комментарий,Статус,СкладскаяОперация,ВсегоМест," +
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

    public static ReceivingOrder MapToReceivingOrder(Document_ПриходныйОрдерНаТовары fetchedItem)
    {
        var items = fetchedItem.Товары
            .Select(x => new ReceivingOrderItem
            {
                ReceivingOrderId = x.Ref_Key,
                LineNumber = x.LineNumber,
                StockKeepingUnitId = x.Номенклатура_Key,
                PlanQuantity = x.КоличествоУпаковок, // TODO: КоличествоУпаковок или Количество?
                FactQuantity = 0
            })
            .ToList();

        return new ReceivingOrder
        {
            Id = fetchedItem.Ref_Key,
            Posted = fetchedItem.Posted,
            DeletionMark = fetchedItem.DeletionMark,
            Date = fetchedItem.Date,
            Number = fetchedItem.Number,
            Comment = fetchedItem.Комментарий,
            WarehouseId = fetchedItem.Склад_Key,
            ReceivingLocationId = Guid.Empty,
            Status = ODataEnumMapper.Parse<ReceivingOrderStatus>(fetchedItem.Статус),
            Queue = ODataEnumMapper.Parse<ReceivingOrderQueue>(fetchedItem.Доброга_ТипОчереди),
            WarehouseOperation = ODataEnumMapper.Parse<WarehouseOperation>(fetchedItem.СкладскаяОперация),
            BusinessOperation = ODataEnumMapper.Parse<BusinessOperation>(fetchedItem.ХозяйственнаяОперация),
            StartedAtUtc = null,
            CompletedAtUtc = null,
            SenderId = fetchedItem.Отправитель,
            SenderType = fetchedItem.Отправитель_Type.TrimODataPrefix(),
            BaseOrderId = fetchedItem.Распоряжение,
            BaseOrderType = fetchedItem.Распоряжение_Type.TrimODataPrefix(),
            Items = items
        };
    }
}




