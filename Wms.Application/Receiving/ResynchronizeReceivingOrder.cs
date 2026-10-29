using Wms.Application.Authorization;
using Wms.Common;
using Wms.Domain.Enums;

namespace Wms.Application.Receiving;

public sealed record ReceivingSourceRefreshResult(
    Guid RefKey, bool Succeeded, DateTimeOffset? LastSuccessfulCheckAtUtc,
    string? Error);

public sealed record ReceivingOrderRefreshResult(
    Guid OrderId, bool HasSources, bool AllSucceeded,
    IReadOnlyList<ReceivingSourceRefreshResult> Sources);

public sealed class ResynchronizeReceivingOrder(
    IReceivingWorkQueryStore orders, GetReceivingSources sources,
    SynchronizeReceivingSource synchronize, WarehousePermission permission)
{
    public async Task<OperationResult<ReceivingOrderRefreshResult>> ExecuteAsync(
        Guid orderId, string userId, CancellationToken ct = default)
    {
        if (orderId == Guid.Empty)
            return OperationError.Invalid("Приходный ордер обязателен.");
        var access = await permission.RequireManagerAsync(userId, ct);
        if (!access.IsSuccess)
            return access.Error!;

        var order = await orders.GetOrderAsync(orderId, userId, ct);
        if (order is null)
            return OperationError.NotFound("Приходный ордер не найден.");
        if (order.Status == ReceivingOrderStatus.Deleted)
            return OperationError.Conflict("Удалённый ордер нельзя сверять.");
        if (order.SourceRefKeys.Count == 0)
            return new ReceivingOrderRefreshResult(orderId, false, false, []);

        var errors = new Dictionary<Guid, string>();
        foreach (var refKey in order.SourceRefKeys.Distinct())
        {
            var refreshed = await synchronize.ExecuteAsync(
                refKey, new ReceivingSourceCheckAudit(orderId, userId), ct);
            if (!refreshed.IsSuccess)
                errors[refKey] = refreshed.Error!.Message;
        }

        var current = await sources.GetByRefKeysAsync(order.SourceRefKeys, ct);
        var byRefKey = current.ToDictionary(x => x.RefKey);
        var results = order.SourceRefKeys.Distinct().Select(refKey =>
        {
            byRefKey.TryGetValue(refKey, out var source);
            var error = errors.GetValueOrDefault(refKey)
                ?? source?.LastAttemptError;
            return new ReceivingSourceRefreshResult(refKey,
                error is null && source?.LastSuccessfulCheckAtUtc is not null,
                source?.LastSuccessfulCheckAtUtc, error);
        }).ToArray();
        return new ReceivingOrderRefreshResult(orderId, true,
            results.All(x => x.Succeeded), results);
    }
}
