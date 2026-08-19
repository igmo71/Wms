using System.Text.Json.Serialization;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Integration.OneS.Models;

internal class Document_РасходныйОрдерНаТовары
{
    public Guid Ref_Key { get; set; }
    public bool DeletionMark { get; set; }
    public bool Posted { get; set; }
    public string? Number { get; set; }
    public DateTime Date { get; set; }
    public Guid Склад_Key { get; set; }
    public string? Статус { get; set; }
    public string? СкладскаяОперация { get; set; }
    public Guid Получатель { get; set; }
    public string? Получатель_Type { get; set; }
    public string? Комментарий { get; set; }
    public string? Доброга_ТипОчереди { get; set; }
    public Guid? Доброга_НаправлениеДоставки_Key { get; set; }
    public DateTime? Доброга_ПланируемаяДатаОтгрузки { get; set; }

    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int ВсегоМест { get; set; }
    public List<Document_РасходныйОрдерНаТовары_ТоварыПоРаспоряжениям> ТоварыПоРаспоряжениям { get; set; } = [];
    public List<Document_РасходныйОрдерНаТовары_ОтгружаемыеТовары> ОтгружаемыеТовары { get; set; } = [];

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

    internal static List<Document_РасходныйОрдерНаТовары_ОтгружаемыеТовары> GetUnexpectedPreparedItemActions(
        Document_РасходныйОрдерНаТовары document) =>
        ODataEnumMapper.Parse<ShippingOrderStatus>(document.Статус) != ShippingOrderStatus.Prepared
            ? []
            : [.. document.ОтгружаемыеТовары
                .Where(IsRegularShippingItem)
                .Where(x => ODataEnumMapper.Parse<ShippingOrderAction>(x.Действие) != ShippingOrderAction.PickUp)];

    internal static bool IsRegularShippingItem(Document_РасходныйОрдерНаТовары_ОтгружаемыеТовары item) =>
        !item.ЭтоУпаковочныйЛист && item.ЭтоСлужебнаяСтрокаПустогоУпаковочногоЛиста == 0;

    internal static ShippingOrderImportSnapshot MapToImportSnapshot(
        Document_РасходныйОрдерНаТовары fetchedDocument)
    {
        var items = fetchedDocument.ОтгружаемыеТовары
            .Where(IsRegularShippingItem)
            .Select(x => new ShippingOrderItemImportSnapshot(
                x.LineNumber,
                x.Номенклатура_Key,
                x.КоличествоУпаковок)) // TODO: КоличествоУпаковок или Количество?
            .ToList();

        var baseItems = fetchedDocument.ТоварыПоРаспоряжениям
            .Select(x => new ShippingOrderBaseItemImportSnapshot(
                x.LineNumber,
                x.Номенклатура_Key,
                x.Количество,
                x.Распоряжение,
                x.Распоряжение_Type.TrimODataPrefix()))
            .ToList();

        return new ShippingOrderImportSnapshot(
            fetchedDocument.Ref_Key,
            fetchedDocument.DeletionMark,
            fetchedDocument.Posted,
            fetchedDocument.Number,
            fetchedDocument.Date,
            fetchedDocument.Склад_Key,
            fetchedDocument.Комментарий,
            ODataEnumMapper.Parse<ShippingOrderStatus>(fetchedDocument.Статус),
            ODataEnumMapper.Parse<ShippingOrderQueue>(fetchedDocument.Доброга_ТипОчереди),
            fetchedDocument.Доброга_ПланируемаяДатаОтгрузки?.Date == DateTime.MinValue.Date
                ? null
                : fetchedDocument.Доброга_ПланируемаяДатаОтгрузки,
            fetchedDocument.Доброга_НаправлениеДоставки_Key == Guid.Empty
                ? null
                : fetchedDocument.Доброга_НаправлениеДоставки_Key,
            ODataEnumMapper.Parse<WarehouseOperation>(fetchedDocument.СкладскаяОперация),
            fetchedDocument.Получатель,
            ODataEnumMapper.Parse<PartyType>(fetchedDocument.Получатель_Type),
            items,
            baseItems);
    }
}
