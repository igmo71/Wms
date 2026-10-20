using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Wms.Application.Authorization;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.Receiving;

public sealed record PreviewReceivingOrderSourceChangeRequest(
    Guid OrderId, Guid SourceRefKey, ReceivingSourceChangeKind Kind, string UserId);

public sealed record ReceivingSourceCompositionPlanChange(
    Guid SkuId, string? SkuName, bool BeforePresent, decimal? BeforePlan,
    bool AfterPresent, decimal? AfterPlan, decimal? FactQuantity,
    decimal? ProjectedExcess);

public sealed record ReceivingSourceCompositionPreview(
    Guid OrderId, string OrderNumber, ReceivingSourceChangeKind Kind,
    Guid SourceRefKey, string? SourceNumber, Guid? SourceWarehouseId,
    string? SourceWarehouseName, bool? SourcePosted, bool? SourceDeletionMark,
    string? SourceCheckError, IReadOnlyList<ReceivingSourceCompositionPlanChange> PlanChanges,
    bool HasUnknownSku, string FactPreservationWarning, string StateToken);

public sealed class PreviewReceivingOrderSourceChange(
    IReceivingOrderSourceChangeStore store, IReceivingSourceParser parser,
    SynchronizeReceivingSource synchronize, IReceivingReferenceNames names,
    WarehousePermission permission)
{
    public async Task<OperationResult<ReceivingSourceCompositionPreview>> ExecuteAsync(
        PreviewReceivingOrderSourceChangeRequest request, CancellationToken ct = default)
    {
        if (request is null || request.OrderId == Guid.Empty || request.SourceRefKey == Guid.Empty
            || !Enum.IsDefined(request.Kind))
            return OperationError.Invalid("Ордер, источник и действие обязательны.");

        var access = await permission.RequireManagerAsync(request.UserId, ct);
        if (!access.IsSuccess)
            return access.Error!;

        var state = await store.LoadPreviewAsync(request.OrderId, request.SourceRefKey, ct);
        if (state.Order is null)
            return OperationError.NotFound("Приходный ордер не найден.");
        if (state.Order.Status is not (ReceivingOrderStatus.ReadyForReceiving
            or ReceivingOrderStatus.InReceiving or ReceivingOrderStatus.Received))
            return OperationError.Conflict("Состав источников нельзя изменить в текущем статусе ордера.");

        if (request.Kind == ReceivingSourceChangeKind.Attach)
        {
            if (state.TargetLinkedOrderId is Guid linkedOrderId)
                return OperationError.Conflict(linkedOrderId == request.OrderId
                    ? "Источник уже привязан к этому ордеру."
                    : "Источник уже привязан к другому ордеру.");

            var refreshed = await synchronize.ExecuteAsync(request.SourceRefKey, ct);
            if (!refreshed.IsSuccess)
                return refreshed.Error!;
            state = await store.LoadPreviewAsync(request.OrderId, request.SourceRefKey, ct);
            if (state.Order is null)
                return OperationError.NotFound("Приходный ордер не найден.");
        }

        var order = state.Order!;
        if (order.Status is not (ReceivingOrderStatus.ReadyForReceiving
            or ReceivingOrderStatus.InReceiving or ReceivingOrderStatus.Received))
            return OperationError.Conflict("Состав источников нельзя изменить в текущем статусе ордера.");
        if (request.Kind == ReceivingSourceChangeKind.Attach)
        {
            if (state.TargetLinkedOrderId is Guid linkedOrderId)
                return OperationError.Conflict(linkedOrderId == order.Id
                    ? "Источник уже привязан к этому ордеру."
                    : "Источник уже привязан к другому ордеру.");
        }
        else if (state.TargetLink is null)
            return OperationError.Conflict("Источник не привязан к этому ордеру.");

        var target = state.Target;
        if (target?.Version is null || target.Source.LastSuccessfulCheckAtUtc is null)
            return OperationError.NotFound("Источник не найден или ещё не сверен с 1С.");
        var targetParsed = parser.Parse(request.SourceRefKey, target.Version.SnapshotJson);
        if (!targetParsed.IsSuccess)
            return targetParsed.Error!;
        if (request.Kind == ReceivingSourceChangeKind.Attach)
        {
            if (targetParsed.Value!.WarehouseId is not Guid warehouseId)
                return OperationError.Conflict("Склад источника не подтверждён.");
            if (warehouseId != order.WarehouseId)
                return OperationError.Conflict("Склад источника отличается от склада ордера.");
        }

        var included = request.Kind == ReceivingSourceChangeKind.Attach
            ? state.ActiveSources.Append(target).ToArray()
            : state.ActiveSources.Where(source => source.Source.Id != target.Source.Id).ToArray();
        var contributions = new List<(ParsedReceivingSource Source, ReceivingSourcePlan Plan)>();
        var hasUnknownSku = false;
        foreach (var source in included)
        {
            if (source.Version is null)
                return OperationError.Conflict("Версия связанного источника не найдена.");
            var parsed = source.Source.Id == target.Source.Id
                ? targetParsed : parser.Parse(source.Source.RefKey, source.Version.SnapshotJson);
            if (!parsed.IsSuccess)
                return parsed.Error!;
            var plan = source.Version.CalculatePlan();
            hasUnknownSku |= plan.HasUnknownSku;
            contributions.Add((parsed.Value!, plan));
        }

        var projected = ReceivingOrderPlanProjection.Sum(order.WarehouseId, contributions);
        var items = order.Items.ToDictionary(item => item.StockKeepingUnitId);
        var postedSkuIds = ReceivingCurrentComposition.PostedSkuIds(order);
        var beforeSkuIds = ReceivingCurrentComposition.SourceSkuIds(state.ActiveSources);
        beforeSkuIds.UnionWith(postedSkuIds);
        var afterSkuIds = ReceivingCurrentComposition.SourceSkuIds(included);
        afterSkuIds.UnionWith(postedSkuIds);
        var changed = beforeSkuIds.Concat(afterSkuIds).Distinct()
            .OrderBy(skuId => skuId)
            .Select(skuId =>
            {
                var beforePresent = beforeSkuIds.Contains(skuId);
                var beforePlan = items.TryGetValue(skuId, out var item)
                    ? item.CurrentPlanQuantity : 0m;
                var afterPresent = afterSkuIds.Contains(skuId);
                var afterPlan = projected.TryGetValue(skuId, out var value) ? value : 0m;
                var fact = item?.FactQuantity;
                return new
                {
                    SkuId = skuId, BeforePresent = beforePresent, BeforePlan = beforePlan,
                    AfterPresent = afterPresent, AfterPlan = (decimal?)afterPlan,
                    Fact = fact,
                    ProjectedExcess = fact is decimal quantity && afterPlan is decimal plan
                        && quantity > plan ? quantity - plan : (decimal?)null
                };
            })
            .Where(change => change.BeforePresent != change.AfterPresent
                || change.BeforePlan != change.AfterPlan)
            .ToArray();

        var warehouseIds = targetParsed.Value!.WarehouseId is Guid sourceWarehouseId
            ? new[] { sourceWarehouseId } : Array.Empty<Guid>();
        var referenceNames = await names.GetAsync(warehouseIds,
            changed.Select(change => change.SkuId).ToArray(), [], ct);
        var changes = changed.Select(change => new ReceivingSourceCompositionPlanChange(
            change.SkuId, referenceNames.Skus.GetValueOrDefault(change.SkuId),
            change.BeforePresent, change.BeforePlan, change.AfterPresent, change.AfterPlan,
            change.Fact, change.ProjectedExcess))
            .ToArray();

        return new ReceivingSourceCompositionPreview(order.Id, order.Number, request.Kind,
            request.SourceRefKey, targetParsed.Value.Number, targetParsed.Value.WarehouseId,
            targetParsed.Value.WarehouseId is Guid id
                ? referenceNames.Warehouses.GetValueOrDefault(id) : null,
            targetParsed.Value.Posted, targetParsed.Value.DeletionMark,
            target.Source.LastAttemptError, changes, hasUnknownSku,
            "Проведённый факт и движения сохраняются.",
            ReceivingSourceCompositionToken.Create(state, request.Kind, request.SourceRefKey));
    }
}

internal static class ReceivingSourceCompositionToken
{
    public static string Create(ReceivingSourceChangeState state,
        ReceivingSourceChangeKind kind, Guid sourceRefKey)
    {
        var order = state.Order ?? throw new InvalidOperationException("Ордер обязателен.");
        var canonical = JsonSerializer.Serialize(new
        {
            order.Id,
            order.OperationalRevision,
            order.WarehouseId,
            order.Status,
            Kind = kind,
            SourceRefKey = sourceRefKey,
            TargetSourceId = state.Target?.Source.Id,
            TargetVersionId = state.Target?.Source.LastSuccessfulVersionId,
            TargetCheckTime = state.Target?.Source.LastSuccessfulCheckAtUtc,
            state.TargetLinkedOrderId,
            TargetLinkId = state.TargetLink?.Id,
            ActiveLinks = state.ActiveLinks.OrderBy(link => link.Id).Select(link => new
            {
                link.Id, link.ReceivingSourceDocumentId, link.ReceivingOrderId
            }).ToArray(),
            SourceVersions = state.ActiveSources.OrderBy(source => source.Source.Id)
                .Select(source => new
                {
                    source.Source.Id,
                    source.Source.LastSuccessfulVersionId,
                    source.Source.LastSuccessfulCheckAtUtc
                }).ToArray(),
            CurrentPlan = order.Items.OrderBy(item => item.StockKeepingUnitId)
                .Select(item => new
                {
                    item.StockKeepingUnitId, item.CurrentPlanQuantity, item.FactQuantity
                })
                .ToArray()
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
