using Wms.Common;
using Wms.Domain;

namespace Wms.Application.Picking;

public interface IShippingSourceStore
{
    Task<OperationResult<Guid>> SaveAsync(
        ShippingOrderImportSnapshot source,
        string rawDocumentJson,
        IReadOnlyDictionary<Guid, decimal> planBySku,
        DateTimeOffset checkedAtUtc,
        CancellationToken ct);

    Task RecordFailureAsync(Guid refKey, string? rawDocumentJson,
        string error, DateTimeOffset checkedAtUtc, CancellationToken ct);
}

public sealed class SynchronizeShippingSource(
    IShippingSourceReader reader,
    IShippingSourceStore store,
    TimeProvider clock)
{
    public async Task<OperationResult<Guid>> ExecuteAsync(Guid refKey, CancellationToken ct = default)
    {
        if (refKey == Guid.Empty)
            return OperationError.Invalid("Ref_Key расходного документа 1С обязателен.");

        var read = await reader.ReadAsync(refKey, ct);
        if (!read.IsSuccess)
        {
            await store.RecordFailureAsync(refKey, null, read.Error!.Message,
                clock.GetUtcNow(), ct);
            return read.Error!;
        }

        var current = read.Value!;
        if (current.RefKey != refKey)
            return await FailAsync(refKey, null,
                "1С вернула другой расходный документ.", ct);

        var projection = Project(current);
        if (!projection.IsSuccess)
            return await FailAsync(refKey, current.RawDocumentJson,
                projection.Error!.Message, ct);

        var (source, plan) = projection.Value!;
        var saved = await store.SaveAsync(source, current.RawDocumentJson,
            plan, clock.GetUtcNow(), ct);
        if (!saved.IsSuccess)
            return await FailAsync(refKey, current.RawDocumentJson,
                saved.Error!.Message, ct);
        return saved;
    }

    private async Task<OperationResult<Guid>> FailAsync(Guid refKey,
        string? rawDocumentJson, string error, CancellationToken ct)
    {
        await store.RecordFailureAsync(refKey, rawDocumentJson, error,
            clock.GetUtcNow(), ct);
        return OperationError.Failure(error);
    }

    private static OperationResult<(
        ShippingOrderImportSnapshot Source,
        IReadOnlyDictionary<Guid, decimal> Plan)> Project(ShippingSourceSnapshot current)
    {
        if (current.WarehouseId is not Guid warehouseId || warehouseId == Guid.Empty
            || current.Date is not DateTime date || date == default
            || current.Posted is not bool posted
            || current.DeletionMark is not bool deletionMark
            || current.ReceiverId is not Guid receiverId || receiverId == Guid.Empty
            || current.ReceiverType is not { } receiverType)
            return OperationError.Failure("Реквизиты расходного документа 1С не прочитаны полностью.");

        var items = new List<ShippingOrderItemImportSnapshot>();
        var plan = new Dictionary<Guid, decimal>();
        foreach (var line in current.Items)
        {
            if (line.IsPackagingList is null || line.IsServiceLine is null)
                return OperationError.Failure("Тип строки отгружаемых товаров 1С не определён.");
            if (!line.IsRegular)
                continue;
            if (line.StockKeepingUnitId is not Guid skuId || skuId == Guid.Empty
                || line.Quantity is not decimal quantity
                || line.PackageQuantity is not decimal packageQuantity
                || line.Action == Wms.Domain.Enums.ShippingOrderAction.Unknown)
                return OperationError.Failure("Строка отгружаемых товаров 1С содержит неизвестный SKU, количество или действие.");

            items.Add(new ShippingOrderItemImportSnapshot(
                line.LineNumber, skuId, packageQuantity, quantity, line.Action));
            var total = plan.GetValueOrDefault(skuId) + packageQuantity;
            if (!WarehouseQuantity.IsNonNegative(total))
                return OperationError.Failure("Суммарное количество SKU в документе 1С превышает допустимое.");
            plan[skuId] = total;
        }

        var basisItems = new List<ShippingOrderBaseItemImportSnapshot>();
        foreach (var line in current.BasisItems)
        {
            if (line.StockKeepingUnitId is not Guid skuId || skuId == Guid.Empty
                || line.Quantity is not decimal quantity)
                return OperationError.Failure("Строка товаров по распоряжениям 1С содержит неизвестный SKU или количество.");
            basisItems.Add(new ShippingOrderBaseItemImportSnapshot(
                line.LineNumber, skuId, quantity,
                line.BasisOrderId ?? Guid.Empty, line.BasisOrderType));
        }

        var source = new ShippingOrderImportSnapshot(
            current.RefKey, deletionMark, posted, current.Number, date,
            warehouseId, current.Comment, current.Status, current.Queue,
            current.PlannedShippingDate, current.DeliveryDirectionId,
            current.WarehouseOperation, receiverId, receiverType,
            items, basisItems);
        return OperationResult<(
            ShippingOrderImportSnapshot Source,
            IReadOnlyDictionary<Guid, decimal> Plan)>.Success((source, plan));
    }
}
