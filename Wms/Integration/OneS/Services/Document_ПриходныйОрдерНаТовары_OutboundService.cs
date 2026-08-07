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
        await SwitchStatusAsync("ВРаботе", orderId, ct);

    internal async Task<ServiceResult> CompleteOrderAsync(Guid orderId, CancellationToken ct) =>
        await SwitchStatusAsync("Принят", orderId, ct);

    private async Task<ServiceResult> SwitchStatusAsync(string status, Guid orderId, CancellationToken ct)
    {
        using var scope = logger.BeginScope("SwitchStatus {OrderId} {Status}", orderId, status);

        var patchUri = Document.PatchUri(orderId.ToString());

        var patchCommand = new StatusOrderCommand(status);

        var patchResult = await oneCClient.PatchValueAsync<StatusOrderCommand, Document>(patchUri, patchCommand, ct);

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
            .Select(x => new PatchItem
            {
                Ref_Key = x.ReceivingOrderId,
                LineNumber = x.LineNumber,
                Номенклатура_Key = x.StockKeepingUnitId,
                Количество = x.FactQuantity,
                КоличествоУпаковок = x.FactQuantity,
                Комментарий = x.Comment
            })
            .ToList();

        var patchBody = new PatchBody { Товары = patchItems };

        var patchUri = Document.PatchUri(orderId.ToString());

        var patchResult = await oneCClient.PatchValueAsync<PatchBody, Document>(patchUri, patchBody, ct);

        if (patchResult is null)
        {
            return ServiceError.Failure<ReceivingOrder>("Failed to update external document items");
        }

        return ServiceResult.Success();
    }

    internal class PatchBody
    {
        public List<PatchItem> Товары { get; set; } = [];
    }

    internal class PatchItem
    {
        public Guid Ref_Key { get; set; }
        public int LineNumber { get; set; }
        public Guid Номенклатура_Key { get; set; }
        public double КоличествоУпаковок { get; set; }
        public double Количество { get; set; }
        public string? Комментарий { get; set; }
    }
}
