using Wms.Common;
using Wms.Domain;

namespace Wms.Application.Receiving;

public sealed class CreateReceivingOrder(
    IReceivingOrderStore store, IReceivingSourceParser parser, TimeProvider clock)
{
    public async Task<OperationResult<Guid>> ExecuteAsync(
        IReadOnlyCollection<Guid> refKeys, CancellationToken ct = default)
    {
        if (refKeys is null || refKeys.Count == 0 || refKeys.Contains(Guid.Empty))
            return OperationError.Invalid("Выберите хотя бы один приходный документ 1С.");

        var distinctKeys = refKeys.Distinct().ToArray();
        var states = await store.LoadSourcesAsync(distinctKeys, ct);
        if (states.Count != distinctKeys.Length)
            return OperationError.NotFound("Не все выбранные документы 1С найдены в WMS.");

        Guid? warehouseId = null;
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

        return await store.CreateAsync(order, links, items, expectations, ct);
    }
}
