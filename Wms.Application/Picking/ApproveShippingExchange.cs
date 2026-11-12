using System.Text.Json;
using Wms.Application.Authorization;
using Wms.Application.CommandExecution;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.Picking;

public sealed record ApproveShippingExchangeRequest(Guid RequestId,
    Guid PickingAssemblyId, Guid ShippingOrderId, long ExpectedRevision,
    string ReviewedSourceJson, string Reason, string UserId);

public sealed class ApproveShippingExchange(
    IShippingExchangeStore store, IShippingSourceReader reader,
    ICommandExecutionStore receipts, CommandExecutor executor,
    WarehousePermission permission,
    TimeProvider clock)
{
    private const string CommandType = "picking.exchange.approve";

    public async Task<OperationResult<Guid>> ExecuteAsync(
        ApproveShippingExchangeRequest request, CancellationToken ct = default)
    {
        if (request is null || request.RequestId == Guid.Empty
            || request.PickingAssemblyId == Guid.Empty
            || request.ShippingOrderId == Guid.Empty
            || request.ExpectedRevision < 0
            || string.IsNullOrWhiteSpace(request.ReviewedSourceJson)
            || string.IsNullOrWhiteSpace(request.Reason)
            || request.Reason.Trim().Length > 500
            || string.IsNullOrWhiteSpace(request.UserId)
            || request.UserId.Length > 36)
            return OperationError.Invalid(
                "Укажите документ, просмотренное состояние и причину решения Manager.");
        var access = await permission.RequireManagerAsync(request.UserId, ct);
        if (!access.IsSuccess) return access.Error!;
        var hash = CommandExecutor.ComputeHash(
            $"{request.PickingAssemblyId:N}:{request.ShippingOrderId:N}:"
            + $"{request.ExpectedRevision}:{request.Reason.Trim()}:"
            + CommandExecutor.ComputeHash(request.ReviewedSourceJson));
        var receipt = await receipts.FindReceiptAsync(request.UserId,
            CommandType, request.RequestId, ct);
        if (receipt is not null)
            return receipt.RequestHash == hash
                ? receipt.ResultResourceId
                : OperationError.Conflict(
                    "Этот идентификатор решения уже использован с другими данными.");
        var acquired = await store.AcquireAsync(request.ShippingOrderId, ct);
        if (!acquired.IsSuccess) return acquired.Error!;
        await using var lease = acquired.Value!;

        var state = await store.ReadAsync(request.PickingAssemblyId,
            request.ShippingOrderId, ct);
        if (state.AssemblyStatus != PickingAssemblyStatus.Closed
            || state.Target?.Stage != ShippingExchangeStage.NeedsReview)
            return OperationError.Conflict(
                "Решение доступно только для изменённого РО закрытой сборки.");
        var fresh = await reader.ReadAsync(request.ShippingOrderId, ct);
        if (!fresh.IsSuccess) return fresh.Error!;
        if (fresh.Value!.RawDocumentJson != request.ReviewedSourceJson)
            return OperationError.Conflict(
                "РО 1С изменился после просмотра. Откройте его заново.");
        if (fresh.Value.DeletionMark != false
            || fresh.Value.Posted is null)
            return OperationError.Conflict(
                "РО 1С помечен на удаление или его проведение неизвестно.");

        return await executor.ExecuteAsync(CommandType, request.RequestId,
            hash, request.UserId, async (session, operationCt) =>
            {
                var target = await store.LoadTargetAsync(session,
                    request.PickingAssemblyId, request.ShippingOrderId,
                    operationCt);
                if (target is null)
                    return OperationError.NotFound("Цель обмена РО 1С не найдена.");
                var before = target.ReviewedSourceJson;
                var now = clock.GetUtcNow();
                var approved = target.ApproveReviewedSource(
                    request.ExpectedRevision, now, request.ReviewedSourceJson);
                if (!approved.IsSuccess) return approved.Error!;
                var audit = WarehouseAuditEvent.Create(CommandType, now,
                    request.UserId, null, null, null, null, null,
                    JsonSerializer.Serialize(new
                    {
                        ShippingOrderId = request.ShippingOrderId,
                        Revision = request.ExpectedRevision,
                        SourceHash = CommandExecutor.ComputeHash(before)
                    }),
                    JsonSerializer.Serialize(new
                    {
                        ShippingOrderId = request.ShippingOrderId,
                        target.DecisionRevision, target.OperationalRevision,
                        SourceHash = CommandExecutor.ComputeHash(request.ReviewedSourceJson),
                        Reason = request.Reason.Trim()
                    }),
                    commandUserId: request.UserId,
                    commandType: CommandType, requestId: request.RequestId,
                    pickingAssemblyId: request.PickingAssemblyId);
                if (!audit.IsSuccess) return audit.Error!;
                return new CommandExecutionResult(request.PickingAssemblyId,
                    [audit.Value!]);
            }, ct);
    }
}
