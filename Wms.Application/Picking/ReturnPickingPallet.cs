using System.Text.Json;
using Wms.Application.Authorization;
using Wms.Application.CommandExecution;
using Wms.Common;
using Wms.Domain;

namespace Wms.Application.Picking;

public sealed record ReturnPickingPalletRequest(Guid RequestId, Guid PickingAssemblyId,
    long ExpectedRevision, string ScannedLocationCode, string UserId);

public sealed class ReturnPickingPallet(CommandExecutor executor, IPickingAssemblyStore assemblies,
    WarehousePermission permission, TimeProvider clock)
{
    private const string CommandType = "picking.pallet.return";

    public Task<OperationResult<Guid>> ExecuteAsync(ReturnPickingPalletRequest request,
        CancellationToken ct = default)
    {
        if (request is null || request.RequestId == Guid.Empty
            || request.PickingAssemblyId == Guid.Empty || request.ExpectedRevision < 0
            || string.IsNullOrWhiteSpace(request.UserId))
            return Task.FromResult<OperationResult<Guid>>(
                OperationError.Invalid("Укажите сборку, скан ячейки и сотрудника."));
        var normalized = StorageLocationAddress.Normalize(request.ScannedLocationCode);
        if (!normalized.IsSuccess)
            return Task.FromResult<OperationResult<Guid>>(normalized.Error!);
        var code = normalized.Value!;
        var hash = CommandExecutor.ComputeHash(
            $"{request.PickingAssemblyId:N}:{request.ExpectedRevision}:{code}");
        return executor.ExecuteAsync(CommandType, request.RequestId, hash, request.UserId,
            async (session, token) =>
            {
                var access = await permission.RequireOperatorAsync(request.UserId, token);
                if (!access.IsSuccess) return access.Error!;
                var state = await assemblies.LoadWorkAsync(session,
                    request.PickingAssemblyId, code, token);
                var assembly = state.Assembly;
                if (assembly is null) return OperationError.NotFound("Сборка WMS не найдена.");
                var changed = assembly.RecordPhysicalWork(request.UserId,
                    request.ExpectedRevision);
                if (!changed.IsSuccess) return changed.Error!;
                if (state.Location is null)
                    return OperationError.NotFound("Просканированная ячейка не найдена.");
                var released = await assemblies.ReleasePickingHoldAsync(session,
                    assembly.WarehouseId, state.Location.Id, assembly.Id, token);
                if (!released.IsSuccess) return released.Error!;
                var audit = WarehouseAuditEvent.Create("picking.pallet.returned",
                    clock.GetUtcNow(), request.UserId, null, null, null,
                    null, null, null,
                    JsonSerializer.Serialize(new { LocationId = state.Location.Id }),
                    commandUserId: request.UserId, commandType: CommandType,
                    requestId: request.RequestId, pickingAssemblyId: assembly.Id);
                if (!audit.IsSuccess) return audit.Error!;
                return new CommandExecutionResult(state.Location.Id, [audit.Value!]);
            }, ct);
    }
}
