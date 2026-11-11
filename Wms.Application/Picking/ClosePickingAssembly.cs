using System.Globalization;
using System.Text.Json;
using Wms.Application.Authorization;
using Wms.Application.CommandExecution;
using Wms.Application.Inventory;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.Picking;

public sealed record ClosePickingAssemblyRequest(
    Guid RequestId, Guid PickingAssemblyId, long ExpectedRevision,
    string UserId, bool ManagerOverride = false, string? DecisionReason = null,
    IReadOnlyCollection<PickingAllocationInput>? ManagerAllocations = null,
    IReadOnlyDictionary<Guid, string>? ReviewedSourcesById = null);

public sealed class ClosePickingAssembly(CommandExecutor executor,
    IPickingAssemblyStore assemblies, CheckPickingClosure checker,
    AllocatePickingFact allocator, PostInventoryMovement.Handler posting,
    WarehousePermission permission, TimeProvider clock)
{
    private const string CommandType = "picking.assembly.close";

    public Task<OperationResult<Guid>> ExecuteAsync(
        ClosePickingAssemblyRequest request, CancellationToken ct = default)
    {
        if (request is null || request.RequestId == Guid.Empty
            || request.PickingAssemblyId == Guid.Empty || request.ExpectedRevision < 0
            || string.IsNullOrWhiteSpace(request.UserId) || request.UserId.Length > 36
            || request.ManagerAllocations is { Count: > 0 } && !request.ManagerOverride
            || request.ReviewedSourcesById?.Any(x => x.Key == Guid.Empty
                || string.IsNullOrWhiteSpace(x.Value)) == true
            || request.ManagerOverride && (string.IsNullOrWhiteSpace(request.DecisionReason)
                || request.DecisionReason.Trim().Length > 2000
                || request.ReviewedSourcesById is null))
            return Task.FromResult<OperationResult<Guid>>(
                OperationError.Invalid("Укажите сборку, ревизию и решение руководителя при расхождении."));
        var allocationHash = string.Join(";", (request.ManagerAllocations ?? [])
            .OrderBy(x => x.StockKeepingUnitId).ThenBy(x => x.ShippingOrderId)
            .Select(x => $"{x.ShippingOrderId:N}/{x.StockKeepingUnitId:N}="
                + x.Quantity.ToString(CultureInfo.InvariantCulture)));
        var sourceHash = string.Join(";", (request.ReviewedSourcesById ??
                new Dictionary<Guid, string>()).OrderBy(x => x.Key)
            .Select(x => $"{x.Key:N}={CommandExecutor.ComputeHash(x.Value)}"));
        var hash = CommandExecutor.ComputeHash(
            $"{request.PickingAssemblyId:N}:{request.ExpectedRevision}:" +
            $"{request.ManagerOverride}:{request.DecisionReason?.Trim()}:" +
            $"{allocationHash}:{sourceHash}");
        return executor.ExecuteAsync(CommandType, request.RequestId, hash,
            request.UserId, async (session, token) =>
            {
                var access = request.ManagerOverride
                    ? await permission.RequireManagerAsync(request.UserId, token)
                    : await permission.RequireOperatorAsync(request.UserId, token);
                if (!access.IsSuccess) return access.Error!;
                // Fresh 1C reads happen before LoadWorkAsync opens the serializable
                // warehouse transaction. The receipt lookup already happened.
                var checkedResult = await checker.ExecuteAsync(request.PickingAssemblyId,
                    request.UserId, request.ManagerAllocations, token);
                if (!checkedResult.IsSuccess) return checkedResult.Error!;
                var review = checkedResult.Value!;
                if (review.Revision != request.ExpectedRevision)
                    return OperationError.Conflict("Сборка изменилась. Повторите проверку закрытия.");
                if (request.ManagerOverride ? !review.CanManagerClose : !review.CanOperatorClose)
                    return OperationError.Conflict(
                        review.HardBlocks.Concat(review.ManagerIssues).FirstOrDefault()
                        ?? "Сборка пока не готова к закрытию.");
                if (request.ManagerOverride && (request.ReviewedSourcesById!.Count != review.Sources.Count
                    || review.Sources.Any(x => x.CurrentSourceJson is null
                        || !request.ReviewedSourcesById.TryGetValue(x.ShippingOrderId,
                            out var approved) || approved != x.CurrentSourceJson)))
                    return OperationError.Conflict(
                        "Документы 1С изменились после просмотра руководителем. Откройте их заново.");

                var state = await assemblies.LoadWorkAsync(session,
                    request.PickingAssemblyId, null, token);
                var assembly = state.Assembly;
                if (assembly is null)
                    return OperationError.NotFound("Сборка WMS не найдена.");
                if (assembly.Status != PickingAssemblyStatus.InProgress
                    || assembly.OperationalRevision != request.ExpectedRevision
                    || assembly.ActiveSources.Count != review.Sources.Count)
                    return OperationError.Conflict("Сборка изменилась во время закрытия.");
                if (!request.ManagerOverride && assembly.ResponsibleUserId != request.UserId.Trim())
                    return OperationError.Conflict("Сначала примите сборку на себя.");
                foreach (var sourceCheck in review.Sources)
                {
                    if (!assembly.ActiveSources.Any(x => x.ShippingOrderId == sourceCheck.ShippingOrderId))
                        return OperationError.Conflict("Состав документов сборки изменился.");
                    var source = await assemblies.LoadSourceAsync(session,
                        sourceCheck.ShippingOrderId, token);
                    if (source is null || source.CurrentSourceJson is null
                        || source.CurrentSourceJson != sourceCheck.CurrentSourceJson
                        || source.LastSourceCheckError is not null)
                        return OperationError.Conflict(
                            "РО 1С изменился во время закрытия. Повторите сверку.");
                }
                if (state.Entries.Any(x => x.Quantity > 0
                    && x.ExpirationDate is DateOnly expiry
                    && expiry < DateOnly.FromDateTime(clock.GetLocalNow().DateTime)))
                    return OperationError.Conflict("В факте есть товар с истёкшим сроком годности.");
                var stockReady = await assemblies.ValidatePickingClosureStockAsync(
                    session, assembly, state.Entries, token);
                if (!stockReady.IsSuccess) return stockReady.Error!;
                var allocationResult = allocator.Execute(assembly,
                    state.Entries, request.ManagerAllocations);
                if (!allocationResult.IsSuccess) return allocationResult.Error!;
                var allocation = allocationResult.Value!;
                if (allocation.RequiresManagerDecision.Count > 0)
                    return OperationError.Conflict("Не распределён недобор общего SKU.");
                var now = clock.GetUtcNow();
                var movements = new List<Guid>();
                var picked = state.Entries.Where(x => x.Quantity > 0)
                    .OrderBy(x => x.StorageLocationId)
                    .ThenBy(x => x.StockKeepingUnitId)
                    .ThenBy(x => x.ExpirationDate)
                    .ThenBy(x => x.Id).ToArray();
                for (var index = 0; index < picked.Length; index++)
                {
                    var entry = picked[index];
                    var created = InventoryMovement.Create(Guid.NewGuid(),
                        assembly.WarehouseId, entry.StorageLocationId, null,
                        entry.StockKeepingUnitId, entry.Quantity, now,
                        RecorderType.PickingAssembly, assembly.Id, index + 1,
                        request.UserId, entry.ExpirationDate);
                    if (!created.IsSuccess) return created.Error!;
                    var posted = await posting.HandleAsync(new PostInventoryMovement.Command(
                        created.Value!, InventoryPostingKind.PickingIssue, now,
                        PickingEntryId: entry.Id), session, token);
                    if (!posted.IsSuccess) return posted.Error!;
                    movements.Add(posted.Value);
                }
                await assemblies.ReleasePickingClosureHoldsAsync(session,
                    assembly.Id, token);
                var closed = assembly.Close(request.UserId,
                    request.ExpectedRevision, now);
                if (!closed.IsSuccess) return closed.Error!;
                var reviewedJson = JsonSerializer.Serialize(new
                {
                    Sources = review.Sources.Select(x => new
                    {
                        x.ShippingOrderId, x.CurrentSourceJson
                    }).ToArray()
                });
                var closure = PickingClosure.Create(assembly.Id, state.Entries,
                    allocation.Allocations, movements, reviewedJson, request.UserId,
                    now, request.ManagerOverride, request.DecisionReason);
                if (!closure.IsSuccess) return closure.Error!;
                session.Stage(closure.Value!);
                foreach (var line in allocation.Allocations)
                    session.Stage(line);
                foreach (var source in review.Sources)
                {
                    // This neutral, immutable decision is converted to 1C row
                    // JSON by ShippingTargetBuilder in the exchange stage.
                    var targetJson = JsonSerializer.Serialize(new
                    {
                        Version = 1, Status = ShippingOrderStatus.Shipped.ToString(),
                        ApprovedPlan = assembly.PlanLines.Where(x =>
                            x.ShippingOrderId == source.ShippingOrderId)
                            .Select(x => new { SkuId = x.StockKeepingUnitId,
                                x.Quantity }).ToArray(),
                        Allocation = allocation.Allocations.Where(x =>
                            x.ShippingOrderId == source.ShippingOrderId)
                            .Select(x => new { SkuId = x.StockKeepingUnitId,
                                x.Quantity }).ToArray()
                    });
                    var target = ShippingExchangeTarget.Create(assembly.Id,
                        source.ShippingOrderId, assembly.OperationalRevision,
                        source.CurrentSourceJson!, targetJson);
                    if (!target.IsSuccess) return target.Error!;
                    session.Stage(target.Value!);
                }
                var audit = WarehouseAuditEvent.Create("picking.assembly.closed",
                    now, request.UserId, null, null, null, null, null,
                    JsonSerializer.Serialize(new { Revision = request.ExpectedRevision,
                        Status = PickingAssemblyStatus.InProgress.ToString() }),
                    JsonSerializer.Serialize(new { Revision = assembly.OperationalRevision,
                        Status = assembly.Status.ToString(),
                        EntryCount = picked.Length,
                        DocumentCount = review.Sources.Count,
                        ManagerOverride = request.ManagerOverride,
                        Reason = request.DecisionReason?.Trim() }),
                    movements, commandUserId: request.UserId,
                    commandType: CommandType, requestId: request.RequestId,
                    pickingAssemblyId: assembly.Id);
                if (!audit.IsSuccess) return audit.Error!;
                return new CommandExecutionResult(assembly.Id, [audit.Value!]);
            }, ct);
    }
}
