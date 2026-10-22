using System.Text.Json;
using Wms.Application.CommandExecution;
using Wms.Common;
using Wms.Domain;

namespace Wms.Application.Receiving;

public sealed record CreateReceivingOrderRequest(
    Guid RequestId, IReadOnlyCollection<Guid> SourceRefKeys, string UserId);

public sealed class CreateReceivingOrder(
    IReceivingOrderStore store, IReceivingSourceParser parser, TimeProvider clock)
{
    private const string CommandType = "receiving.order.create";

    public async Task<OperationResult<Guid>> ExecuteAsync(
        CreateReceivingOrderRequest request, CancellationToken ct = default)
    {
        if (request is null || request.RequestId == Guid.Empty
            || string.IsNullOrWhiteSpace(request.UserId) || request.UserId.Length > 36)
            return OperationError.Invalid("Идентификатор запроса и пользователь обязательны.");
        var refKeys = request.SourceRefKeys;
        if (refKeys is null || refKeys.Count == 0 || refKeys.Contains(Guid.Empty))
            return OperationError.Invalid("Выберите хотя бы один приходный документ 1С.");

        var distinctKeys = refKeys.Distinct().ToArray();
        var hash = CommandExecutor.ComputeHash(string.Join(":",
            distinctKeys.Order().Select(x => x.ToString("N"))));
        var existing = await store.FindReceiptAsync(
            request.UserId, CommandType, request.RequestId, ct);
        if (existing is not null)
            return existing.RequestHash == hash ? existing.ResultResourceId
                : OperationError.Conflict("Этот идентификатор запроса уже использован с другими данными.");

        var states = await store.LoadSourcesAsync(distinctKeys, ct);
        if (states.Count != distinctKeys.Length)
            return OperationError.NotFound("Не все выбранные документы 1С найдены в WMS.");

        Guid? warehouseId = null;
        Guid? senderId = null;
        var plans = new Dictionary<Guid, decimal?>();
        var expectations = new List<ReceivingSourceExpectation>(states.Count);
        foreach (var state in states)
        {
            if (state.Version is null || state.Source.LastSuccessfulCheckAtUtc is null)
                return OperationError.Conflict("Источник ещё не был успешно сверен с 1С.");

            var parsed = parser.Parse(state.Source.RefKey, state.Version.SnapshotJson);
            if (!parsed.IsSuccess)
                return parsed.Error!;
            var document = parsed.Value!;
            if (document.WarehouseId is not Guid sourceWarehouse
                || document.Posted is null || document.DeletionMark is null)
                return OperationError.Invalid("У источника нет достоверных реквизитов склада или проведения.");
            if (warehouseId is not null && warehouseId != sourceWarehouse)
                return OperationError.Conflict("Источники относятся к разным складам.");
            warehouseId = sourceWarehouse;
            var sourceSender = ReceivingSourceSenderPolicy.GetId(document);
            if (!sourceSender.IsSuccess)
                return sourceSender.Error!;
            if (senderId is not null && senderId != sourceSender.Value)
                return OperationError.Conflict(ReceivingSourceSenderPolicy.MismatchMessage);
            senderId = sourceSender.Value;

            var sourcePlan = state.Version.CalculatePlan();
            foreach (var (skuId, quantity) in sourcePlan.QuantitiesBySku)
            {
                var contribution = document.Posted == true && document.DeletionMark == false
                    ? quantity : 0m;
                if (!plans.TryGetValue(skuId, out var previous))
                    plans[skuId] = contribution;
                else if (previous is null || contribution is null)
                    plans[skuId] = null;
                else
                {
                    try
                    {
                        var sum = previous.Value + contribution.Value;
                        plans[skuId] = WarehouseQuantity.IsNonNegative(sum) ? sum : null;
                    }
                    catch (OverflowException)
                    {
                        plans[skuId] = null;
                    }
                }
            }
            expectations.Add(new ReceivingSourceExpectation(
                state.Source.Id, state.Version.Id,
                state.Source.LastSuccessfulCheckAtUtc.Value));
        }

        var createdAtUtc = clock.GetUtcNow();
        var created = ReceivingOrder.CreateNew(warehouseId!.Value, createdAtUtc);
        if (!created.IsSuccess)
            return created.Error!;
        var order = created.Value!;
        var assignedSender = order.SetSender(senderId!.Value);
        if (!assignedSender.IsSuccess)
            return assignedSender.Error!;

        var links = new List<ReceivingOrderSourceLink>(states.Count);
        foreach (var state in states)
        {
            var link = ReceivingOrderSourceLink.Create(order.Id, state.Source.Id, createdAtUtc);
            if (!link.IsSuccess)
                return link.Error!;
            links.Add(link.Value!);
        }

        var items = new List<ReceivingOrderItem>(plans.Count);
        foreach (var (skuId, quantity) in plans)
        {
            var item = ReceivingOrderItem.CreateForSku(order.Id, skuId, quantity);
            if (!item.IsSuccess)
                return item.Error!;
            items.Add(item.Value!);
        }

        var receipt = new CommandReceiptSnapshot(
            request.UserId, CommandType, request.RequestId, hash, order.Id);
        var audit = WarehouseAuditEvent.Create("receiving.order.created",
            createdAtUtc, request.UserId, null, order.Id, null, null, null, null,
            JsonSerializer.Serialize(new
            {
                order.WarehouseId,
                SourceCount = states.Count,
                SkuCount = items.Count
            }), sourceVersionIds: expectations.Select(x => x.VersionId).ToArray(),
            commandUserId: request.UserId, commandType: CommandType,
            requestId: request.RequestId);
        if (!audit.IsSuccess)
            return audit.Error!;

        return await store.CreateAsync(order, links, items, expectations,
            receipt, audit.Value!, ct);
    }
}
