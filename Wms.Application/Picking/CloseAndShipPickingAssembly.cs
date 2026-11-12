using System.Text.Json;
using Wms.Application.Authorization;
using Wms.Application.CommandExecution;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.Picking;

public sealed record CloseAndShipResult(Guid PickingAssemblyId,
    IReadOnlyList<ShippingExchangeResult> Documents)
{
    public bool AllConfirmed => Documents.All(x =>
        x.Stage == ShippingExchangeStage.PostConfirmed);
}

public sealed class CloseAndShipPickingAssembly(
    ClosePickingAssembly close, ExchangeShippingDocument exchange,
    IPickingAssemblyStore assemblies, IShippingExchangeStore exchangeStore,
    ICommandExecutionStore receipts, CommandExecutor executor,
    WarehousePermission permission, TimeProvider clock)
{
    private const string ResponseType = "picking.assembly.close-and-ship";

    public async Task<OperationResult<CloseAndShipResult>> ExecuteAsync(
        ClosePickingAssemblyRequest request, CancellationToken ct = default)
    {
        if (request is null || request.RequestId == Guid.Empty
            || request.PickingAssemblyId == Guid.Empty || request.ExpectedRevision < 0
            || string.IsNullOrWhiteSpace(request.UserId) || request.UserId.Length > 36
            || request.ReviewedSourcesById?.Any(x => x.Key == Guid.Empty
                || string.IsNullOrWhiteSpace(x.Value)) == true)
            return OperationError.Invalid("Укажите сборку, запрос и исполнителя закрытия.");
        var access = await permission.RequireOperatorAsync(request.UserId, ct);
        if (!access.IsSuccess)
            access = await permission.RequireManagerAsync(request.UserId, ct);
        if (!access.IsSuccess) return access.Error!;

        var hash = RequestHash(request);
        var previous = await receipts.FindReceiptAsync(request.UserId,
            ResponseType, request.RequestId, ct);
        if (previous is not null)
            return previous.RequestHash == hash
                ? await ReadResultAsync(request.PickingAssemblyId, ct)
                : OperationError.Conflict(
                    "Этот идентификатор закрытия уже использован с другим решением.");

        var assembly = await assemblies.ReadAssemblyAsync(request.PickingAssemblyId, ct);
        if (assembly is null)
            return OperationError.NotFound("Сборка WMS не найдена.");
        if (assembly.Status == PickingAssemblyStatus.InProgress)
        {
            var closed = await close.ExecuteAsync(request, ct);
            if (!closed.IsSuccess) return closed.Error!;
            assembly = await assemblies.ReadAssemblyAsync(request.PickingAssemblyId, ct);
            if (assembly is null)
                return OperationError.NotFound("Закрытая сборка WMS не найдена.");
        }
        if (assembly.Status != PickingAssemblyStatus.Closed)
            return OperationError.Conflict("Сборка ещё не готова к отгрузке.");

        var documents = new List<ShippingExchangeResult>();
        foreach (var sourceId in assembly.ActiveSources.Select(x => x.ShippingOrderId)
            .Order())
        {
            var knownStage = ShippingExchangeStage.PendingPatch;
            try
            {
                var state = await exchangeStore.ReadAsync(assembly.Id, sourceId, ct);
                knownStage = state.Target?.Stage ?? ShippingExchangeStage.PendingPatch;
                if (state.Target is null)
                {
                    documents.Add(new ShippingExchangeResult(sourceId,
                        ShippingExchangeStage.PendingPatch,
                        "Цель обмена РО 1С не найдена."));
                    continue;
                }
                if (state.Target.IsConfirmed)
                {
                    documents.Add(new ShippingExchangeResult(sourceId,
                        state.Target.Stage, null));
                    continue;
                }
                var exchanged = await exchange.ExecuteAsync(assembly.Id,
                    sourceId, request.UserId, ct);
                if (exchanged.IsSuccess)
                    documents.Add(exchanged.Value!);
                else
                    documents.Add(new ShippingExchangeResult(sourceId,
                        knownStage, exchanged.Error!.Message));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                documents.Add(new ShippingExchangeResult(sourceId,
                    knownStage,
                    $"Обмен РО 1С прерван: {exception.Message}"));
            }
        }

        var response = await executor.ExecuteAsync(ResponseType, request.RequestId,
            hash, request.UserId, (session, token) =>
            {
                var now = clock.GetUtcNow();
                var audit = WarehouseAuditEvent.Create(ResponseType, now,
                    request.UserId, null, null, null, null, null, null,
                    JsonSerializer.Serialize(new
                    {
                        DocumentCount = documents.Count,
                        ConfirmedCount = documents.Count(x =>
                            x.Stage == ShippingExchangeStage.PostConfirmed)
                    }),
                    commandUserId: request.UserId, commandType: ResponseType,
                    requestId: request.RequestId,
                    pickingAssemblyId: request.PickingAssemblyId);
                return Task.FromResult<OperationResult<CommandExecutionResult>>(
                    audit.IsSuccess
                        ? new CommandExecutionResult(request.PickingAssemblyId,
                            [audit.Value!])
                        : audit.Error!);
            }, ct);
        if (!response.IsSuccess) return response.Error!;
        return new CloseAndShipResult(request.PickingAssemblyId, documents);
    }

    private async Task<OperationResult<CloseAndShipResult>> ReadResultAsync(
        Guid assemblyId, CancellationToken ct)
    {
        var assembly = await assemblies.ReadAssemblyAsync(assemblyId, ct);
        if (assembly is null)
            return OperationError.NotFound("Сборка WMS не найдена.");
        var documents = new List<ShippingExchangeResult>();
        foreach (var sourceId in assembly.ActiveSources.Select(x => x.ShippingOrderId)
            .Order())
        {
            var state = await exchangeStore.ReadAsync(assemblyId, sourceId, ct);
            documents.Add(new ShippingExchangeResult(sourceId,
                state.Target?.Stage ?? ShippingExchangeStage.PendingPatch,
                state.Target?.LastError));
        }
        return new CloseAndShipResult(assemblyId, documents);
    }

    private static string RequestHash(ClosePickingAssemblyRequest request)
    {
        var allocations = (request.ManagerAllocations ?? [])
            .OrderBy(x => x.ShippingOrderId)
            .ThenBy(x => x.StockKeepingUnitId)
            .Select(x => new { x.ShippingOrderId, x.StockKeepingUnitId,
                x.Quantity }).ToArray();
        var sources = (request.ReviewedSourcesById ??
                new Dictionary<Guid, string>())
            .OrderBy(x => x.Key)
            .Select(x => new { ShippingOrderId = x.Key,
                SourceHash = CommandExecutor.ComputeHash(x.Value) }).ToArray();
        return CommandExecutor.ComputeHash(JsonSerializer.Serialize(new
        {
            request.PickingAssemblyId, request.ExpectedRevision,
            request.ManagerOverride, Reason = request.DecisionReason?.Trim(),
            Allocations = allocations, Sources = sources
        }));
    }
}
