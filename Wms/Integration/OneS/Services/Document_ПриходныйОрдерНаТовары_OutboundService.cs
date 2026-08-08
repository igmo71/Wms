using Microsoft.Extensions.Logging;
using Wms.Common;
using Wms.Domain;
using Document = Wms.Integration.OneS.Models.Document_ПриходныйОрдерНаТовары;

namespace Wms.Integration.OneS.Services;

public class Document_ПриходныйОрдерНаТовары_OutboundService(
    OneCClient oneCClient,
    ILogger<Document_ПриходныйОрдерНаТовары_OutboundService> logger)
{
    private record StatusOrderCommand(string Статус);

    internal async Task<ServiceResult> StartOrderAsync(Guid orderId, CancellationToken ct) =>
        await SwitchStatusAsync(new StatusOrderCommand("ВРаботе"), orderId, ct); // TODO: Магическая строка

    internal async Task<ServiceResult> CompleteOrderAsync(Guid orderId, CancellationToken ct) =>
        await SwitchStatusAsync(new StatusOrderCommand("Принят"), orderId, ct); // TODO: Магическая строка

    private async Task<ServiceResult> SwitchStatusAsync(StatusOrderCommand statusOrderCommand, Guid orderId, CancellationToken ct)
    {
        using var scope = logger.BeginScope("SwitchStatus {OrderId} {@StatusOrderCommand}", orderId, statusOrderCommand);

        var patchUri = Document.PatchUri(orderId.ToString());

        var patchResult = await oneCClient.PatchValueAsync<StatusOrderCommand, Document>(patchUri, statusOrderCommand, ct);

        if (!patchResult.IsSuccess)
        {
            return patchResult;
        }

        var postUri = Document.PostDocumentUri(orderId.ToString());

        var postResult = await oneCClient.PostValueAsync(postUri, ct);

        if (!postResult.IsSuccess)
        {
            return postResult;
        }

        return ServiceResult.Success();
    }

    internal async Task<ServiceResult> UpdateDocumentItemsAsync(Guid orderId, List<ReceivingOrderItem> receivingOrderItems, CancellationToken ct)
    {
        using var scope = logger.BeginScope("UpdateDocumentItems {OrderId}", orderId);

        var patchItems = receivingOrderItems
            .Select(x => PatchItem.From(x))
            .ToList();

        var patchBody = new PatchBody { Товары = patchItems };

        var patchUri = Document.PatchUri(orderId.ToString());

        var patchResult = await oneCClient.PatchValueAsync<PatchBody, Document>(patchUri, patchBody, ct);

        if (!patchResult.IsSuccess)
        {
            return patchResult;
        }

        return ServiceResult.Success();
    }

    private class PatchBody
    {
        public List<PatchItem> Товары { get; set; } = [];
    }

    private class PatchItem
    {
        public Guid Ref_Key { get; set; }
        public int LineNumber { get; set; }
        public Guid Номенклатура_Key { get; set; }
        public double КоличествоУпаковок { get; set; }
        public double Количество { get; set; }
        public string? Комментарий { get; set; }

        public static PatchItem From(ReceivingOrderItem orderItem) => new()
        {
            Ref_Key = orderItem.ReceivingOrderId,
            LineNumber = orderItem.LineNumber,
            Номенклатура_Key = orderItem.StockKeepingUnitId,
            Количество = orderItem.FactQuantity,
            КоличествоУпаковок = orderItem.FactQuantity,
            Комментарий = orderItem.Comment
        };

    }
}
