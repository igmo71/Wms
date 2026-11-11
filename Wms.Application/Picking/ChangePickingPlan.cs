using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Wms.Application.Authorization;
using Wms.Application.CommandExecution;
using Wms.Common;
using Wms.Domain;

namespace Wms.Application.Picking;

public sealed record ChangePickingPlanRequest(Guid RequestId, Guid PickingAssemblyId,
    Guid ShippingOrderId, long ExpectedRevision,
    IReadOnlyDictionary<Guid, decimal> PlanBySku,
    string ReviewedSourceJson, string Reason, string UserId);

public sealed class ChangePickingPlan(CommandExecutor executor,
    IPickingAssemblyStore assemblies, WarehousePermission permission,
    TimeProvider clock)
{
    private const string CommandType = "picking.plan.change";

    public Task<OperationResult<Guid>> ExecuteAsync(ChangePickingPlanRequest request,
        CancellationToken ct = default)
    {
        if (request is null || request.RequestId == Guid.Empty
            || request.PickingAssemblyId == Guid.Empty || request.ShippingOrderId == Guid.Empty
            || request.ExpectedRevision < 0 || request.PlanBySku is null
            || request.PlanBySku.Any(x => x.Key == Guid.Empty
                || !WarehouseQuantity.IsNonNegative(x.Value))
            || string.IsNullOrWhiteSpace(request.ReviewedSourceJson)
            || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 1000
            || string.IsNullOrWhiteSpace(request.UserId))
            return Task.FromResult<OperationResult<Guid>>(
                OperationError.Invalid("Укажите сборку, документ, просмотренный источник, план и причину."));
        var planHash = string.Join(";", request.PlanBySku.OrderBy(x => x.Key)
            .Select(x => $"{x.Key:N}={x.Value.ToString(CultureInfo.InvariantCulture)}"));
        var hash = CommandExecutor.ComputeHash(
            $"{request.PickingAssemblyId:N}:{request.ShippingOrderId:N}:" +
            $"{request.ExpectedRevision}:{planHash}:{request.ReviewedSourceJson}:" +
            request.Reason.Trim());
        return executor.ExecuteAsync(CommandType, request.RequestId, hash, request.UserId,
            async (session, token) =>
            {
                var access = await permission.RequireManagerAsync(request.UserId, token);
                if (!access.IsSuccess) return access.Error!;
                var assembly = await assemblies.LoadAssemblyAsync(session,
                    request.PickingAssemblyId, token);
                if (assembly is null)
                    return OperationError.NotFound("Сборка WMS не найдена.");
                if (!assembly.ActiveSources.Any(x => x.ShippingOrderId == request.ShippingOrderId))
                    return OperationError.Conflict("РО 1С не привязан к этой сборке.");
                var source = await assemblies.LoadSourceAsync(session,
                    request.ShippingOrderId, token);
                if (source is null || source.WarehouseId != assembly.WarehouseId
                    || source.CurrentSourceJson != request.ReviewedSourceJson
                    || source.LastSourceCheckError is not null)
                    return OperationError.Conflict("Данные РО 1С изменились. Обновите просмотр перед изменением плана.");
                if (!await assemblies.AreKnownSkusAsync(session,
                    request.PlanBySku.Keys.ToArray(), token))
                    return OperationError.Conflict("План содержит неизвестный или удалённый SKU.");
                var before = assembly.PlanLines.Where(x =>
                    x.ShippingOrderId == request.ShippingOrderId)
                    .ToDictionary(x => x.StockKeepingUnitId, x => x.Quantity);
                var changed = assembly.ChangePlan(request.ShippingOrderId,
                    request.PlanBySku, request.ExpectedRevision);
                if (!changed.IsSuccess) return changed.Error!;
                var now = clock.GetUtcNow();
                var reviewedHash = Convert.ToHexString(SHA256.HashData(
                    Encoding.UTF8.GetBytes(request.ReviewedSourceJson)));
                var audits = new List<WarehouseAuditEvent>();
                var summary = WarehouseAuditEvent.Create("picking.plan.changed",
                    now, request.UserId, null, null, null, null, null,
                    JsonSerializer.Serialize(new { ShippingOrderId = request.ShippingOrderId,
                        Revision = request.ExpectedRevision }),
                    JsonSerializer.Serialize(new { ShippingOrderId = request.ShippingOrderId,
                        Revision = assembly.OperationalRevision,
                        ReviewedSourceHash = reviewedHash,
                        source.LastSourceCheckAtUtc, Reason = request.Reason.Trim() }),
                    commandUserId: request.UserId, commandType: CommandType,
                    requestId: request.RequestId, pickingAssemblyId: assembly.Id);
                if (!summary.IsSuccess) return summary.Error!;
                audits.Add(summary.Value!);
                foreach (var skuId in before.Keys.Union(request.PlanBySku.Keys).Order())
                {
                    var previous = before.GetValueOrDefault(skuId);
                    var next = request.PlanBySku.GetValueOrDefault(skuId);
                    if (previous == next && before.ContainsKey(skuId) == request.PlanBySku.ContainsKey(skuId))
                        continue;
                    var lineAudit = WarehouseAuditEvent.Create("picking.plan.line.changed",
                        now, request.UserId, null, null, null, skuId, null,
                        JsonSerializer.Serialize(new { ShippingOrderId = request.ShippingOrderId,
                            Present = before.ContainsKey(skuId), Quantity = previous }),
                        JsonSerializer.Serialize(new { ShippingOrderId = request.ShippingOrderId,
                            Present = request.PlanBySku.ContainsKey(skuId), Quantity = next }),
                        commandUserId: request.UserId, commandType: CommandType,
                        requestId: request.RequestId, pickingAssemblyId: assembly.Id);
                    if (!lineAudit.IsSuccess) return lineAudit.Error!;
                    audits.Add(lineAudit.Value!);
                }
                return new CommandExecutionResult(assembly.Id, audits);
            }, ct);
    }
}
