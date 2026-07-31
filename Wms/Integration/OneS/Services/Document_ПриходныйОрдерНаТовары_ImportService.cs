using Microsoft.Extensions.Logging;
using Wms.Application;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;
using Wms.Integration.OneS.Models;

namespace Wms.Integration.OneS.Services;



internal class Document_ПриходныйОрдерНаТовары_ImportService(
    OneCClient oneCClient,
    ReceivingOrderService receivingOrderService,
    ILogger<Document_ПриходныйОрдерНаТовары_ImportService> logger)
{
    public async Task ImportAsync(string Ref_Key, CancellationToken ct = default)
    {
        var fetchedItem = await GetAsync(Ref_Key, ct);

        if (fetchedItem is null)
            return;

        ReceivingOrder importedOrder = MapFromODataDocument(fetchedItem);

        await receivingOrderService.CreateOrUpdateImporttedOrder(importedOrder, ct);
    }

    private async Task<Document_ПриходныйОрдерНаТовары?> GetAsync(string Ref_Key, CancellationToken ct = default)
    {
        var uri = Document_ПриходныйОрдерНаТовары.GetUri(Ref_Key);

        var rootObject = await oneCClient.GetValueAsync<RootObject<Document_ПриходныйОрдерНаТовары>>(uri, ct);

        var result = rootObject?.Value?[0];

        return result;
    }

    private static ReceivingOrder MapFromODataDocument(Document_ПриходныйОрдерНаТовары fetchedItem)
    {
        var items = fetchedItem.Товары
            .Select(x => new ReceivingOrderItem
            {
                ReceivingOrderId = x.Ref_Key,
                LineNumber = x.LineNumber,
                StockKeepingUnitId = x.Номенклатура_Key,
                PlanQuantity = x.КоличествоУпаковок, // TODO: КоличествоУпаковок или Количество
                FactQuantity = 0
            })
            .ToList();

        return new ReceivingOrder
        {
            Id = fetchedItem.Ref_Key,
            DataVersion = fetchedItem.DataVersion,
            Posted = fetchedItem.Posted,
            DeletionMark = fetchedItem.DeletionMark,
            DateTime = fetchedItem.Date,
            Number = fetchedItem.Number,
            Comment = fetchedItem.Комментарий,
            WarehouseId = fetchedItem.Склад_Key,
            ReceivingLocationId = null,
            Status = ODataEnumMapper.Parse<ReceivingOrderStatus>(fetchedItem.Статус),
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

    internal async Task ImportListAsync(CancellationToken ct)
    {
        throw new NotImplementedException();
    }
}
